using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using NUnit.Framework;
using UnityEditor.Compilation;
using UnityEngine;
using Assembly = System.Reflection.Assembly;
using Object = UnityEngine.Object;

namespace xpTURN.Feedback.Tests.Editor
{
    // the saved fields' readers · the name references · no legacy input: read with the IL helpers of PlayerPrepareTests
    // (reflection — no Cecil, the minimal manifest too). The runtime assemblies are the player assemblies of the package
    // (CompilationPipeline — AppDomain.GetAssemblies is UAC0005 on 6000.6). The editor compile is read: a field read only
    // under UNITY_EDITOR would pass — no field is
    public sealed class DataModelReadersTests
    {
        static readonly Assembly Core = typeof(FeedbackPlayer).Assembly;

        // the saved fields nothing at runtime reads, on purpose
        static readonly Dictionary<string, string> s_Unread = new Dictionary<string, string>
        {
            { "FeedbackItem.m_Label", "the inspector's label for the item — only the editor shows it" },
        };

        static List<Assembly> RuntimeAssemblies()
            => CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies)
                .Select(a => a.name).Where(n => n.StartsWith("xpTURN.Feedback", StringComparison.Ordinal))
                .OrderBy(n => n, StringComparer.Ordinal).Select(n => Assembly.Load(new AssemblyName(n))).ToList();

        static bool Ours(Type t) => t.Assembly.GetName().Name.StartsWith("xpTURN.Feedback", StringComparison.Ordinal);

        // the saved types reachable from roots: bases of ours, field types of ours (list and array elements)
        static List<Type> SavedTypes(IEnumerable<Type> roots)
        {
            var seen = new List<Type>();
            var stack = new Stack<Type>(roots);
            while (stack.Count > 0)
            {
                Type t = stack.Pop();
                if (t == null || seen.Contains(t))
                    continue;
                seen.Add(t);
                for (Type b = t.BaseType; b != null && b != typeof(object) && Ours(b); b = b.BaseType)
                    stack.Push(b);
                foreach (FieldInfo f in DataModelTraitsTests.SavedFieldInfos(t))
                {
                    Type ft = f.FieldType;
                    if (ft.IsArray)
                        ft = ft.GetElementType();
                    else if (ft.IsGenericType && ft.GetGenericTypeDefinition() == typeof(List<>))
                        ft = ft.GetGenericArguments()[0];
                    if (Ours(ft) && !ft.IsEnum && !typeof(Object).IsAssignableFrom(ft) && (ft.IsSerializable || ft.IsValueType))
                        stack.Push(ft);
                }
            }
            return seen;
        }

        // Core's saved types: its components and assets, its serializable classes and structs
        static IEnumerable<Type> CoreRoots()
            => Core.GetTypes().Where(t => !t.IsGenericTypeDefinition && (typeof(Object).IsAssignableFrom(t)
                || (t.IsSerializable && !t.IsEnum && !typeof(Delegate).IsAssignableFrom(t) && !t.Name.StartsWith("<", StringComparison.Ordinal))));

        static List<string> Unread(IEnumerable<Type> types, HashSet<FieldInfo> read)
            => types.SelectMany(t => DataModelTraitsTests.SavedFieldInfos(t).Where(f => !read.Contains(f)).Select(f => t.Name + "." + f.Name))
                .OrderBy(n => n, StringComparer.Ordinal).ToList();

        static HashSet<FieldInfo> RuntimeReads()
        {
            var read = new HashSet<FieldInfo>();
            foreach (Assembly a in RuntimeAssemblies())
                read.UnionWith(PlayerPrepareTests.FieldReads(a));
            return read;
        }

        [Serializable]
        sealed class UnreadControl
        {
            [SerializeField] internal int m_Unread;
        }

        [Test]
        public void FE04_Reg26_SavedFieldsHaveReaders()
        {
            var fails = new List<string>();
            HashSet<FieldInfo> read = RuntimeReads();
            // an item's own fields are read by its implementing assembly — not compiled, not judged
            List<Type> types = SavedTypes(CoreRoots()).Where(t => !typeof(EffectItem).IsAssignableFrom(t) || OptionalAssemblyTests.Implemented(t)).ToList();
            // the PostFX target fields' values are read by the URP assembly only — the same rule for the four value types
            if (!OptionalAssemblyTests.Implemented(typeof(PostFXBloomItem)))
                types.RemoveAll(t => t == typeof(PostFXFloat) || t == typeof(PostFXColor) || t == typeof(PostFXVector2) || t == typeof(PostFXVector4));
            // the blend's struct is read by the Cinemachine assembly only
            if (!OptionalAssemblyTests.Implemented(typeof(CinemachineActivateCameraItem)))
                types.Remove(typeof(CinemachineBlendSpec));
            List<string> unread = Unread(types, read);
            foreach (string field in unread.Where(f => !s_Unread.ContainsKey(f)))
                fails.Add(field + " has no reader at runtime");
            foreach (string field in s_Unread.Keys.Where(f => !unread.Contains(f)))
                fails.Add(field + " is read now — take it off the exceptions");
            if (types.Count < 60)
                fails.Add("only " + types.Count + " saved types");
            List<string> control = Unread(SavedTypes(new[] { typeof(UnreadControl) }), read);
            if (!control.SequenceEqual(new[] { "UnreadControl.m_Unread" }))
                fails.Add("the control: " + string.Join(" ", control));
            Assert.That(fails, Is.Empty);
        }

        // the types a registering method names: the generic arguments of its Register<T> calls
        static List<Type> Registered(MethodBase method)
        {
            var types = new List<Type>();
            foreach ((OpCode op, int token) in PlayerPrepareTests.Operands(method))
            {
                if (op.OperandType == OperandType.InlineMethod && method.Module.ResolveMethod(token) is MethodInfo m && m.IsGenericMethod && m.Name == "Register")
                    types.AddRange(m.GetGenericArguments());
            }
            return types;
        }

        static void MissingOne()
        {
            FeedbackImplementations.Register<PositionItem>(item => null);
            FeedbackImplementations.Register<RotationItem>(item => null);
        }

        [Test]
        public void FE05_NameReferencesAreComplete()
        {
            var fails = new List<string>();
            const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            List<Type> effects = Core.GetTypes().Where(t => !t.IsAbstract && typeof(EffectItem).IsAssignableFrom(t)).OrderBy(t => t.Name).ToList();
            List<Type> registered = Registered(typeof(CoreImplementations).GetMethod("RegisterAll", Static));
            // Core's items in RegisterAll · an optional assembly's in its own registering methods (where it compiles) — none in the
            // other's. An optional assembly's at its SubsystemRegistration (the editor's load registers too, so a play in the editor
            // never misses the runtime's)
            foreach (Type t in effects.Where(t => OptionalAssemblyTests.ImplementationOf(t).Length == 0).Except(registered))
                fails.Add(t.Name + " is not in RegisterAll");
            foreach (Type t in registered.Where(t => OptionalAssemblyTests.ImplementationOf(t).Length > 0))
                fails.Add(t.Name + " is in RegisterAll — its implementation is " + OptionalAssemblyTests.ImplementationOf(t));
            foreach (IGrouping<string, Type> group in effects.Where(t => OptionalAssemblyTests.ImplementationOf(t).Length > 0 && OptionalAssemblyTests.Implemented(t)).GroupBy(OptionalAssemblyTests.ImplementationOf))
            {
                List<MethodInfo> methods = Assembly.Load(new AssemblyName(group.Key)).GetTypes()
                    .SelectMany(t => t.GetMethods(Static | BindingFlags.DeclaredOnly)).ToList();
                List<Type> registeredThere = methods.SelectMany(Registered).ToList();
                List<Type> onLoad = methods.Where(m => m.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>()?.loadType == RuntimeInitializeLoadType.SubsystemRegistration)
                    .SelectMany(Registered).ToList();
                foreach (Type t in group.Except(registeredThere))
                    fails.Add(t.Name + " is not registered in " + group.Key);
                foreach (Type t in group.Intersect(registeredThere).Except(onLoad))
                    fails.Add(t.Name + " is not registered at " + group.Key + "'s SubsystemRegistration");
            }
            foreach (Type t in registered.Where(t => registered.Count(r => r == t) > 1).Distinct())
                fails.Add(t.Name + " is registered twice");
            List<Type> motions = Core.GetTypes().Where(t => !t.IsAbstract && typeof(FeedbackMotion).IsAssignableFrom(t)).ToList();
            List<Type> named = PlayerPrepareTests.TypeTokens(typeof(MotionEvaluators).GetMethod("Create", Static)).ToList();
            foreach (Type t in motions.Except(named))
                fails.Add(t.Name + " is not in MotionEvaluators.Create");
            if (effects.Count < 9 || motions.Count < 11)
                fails.Add("effects " + effects.Count + " · motions " + motions.Count);
            List<Type> control = Registered(typeof(DataModelReadersTests).GetMethod(nameof(MissingOne), Static));
            if (!control.SequenceEqual(new[] { typeof(PositionItem), typeof(RotationItem) }))
                fails.Add("the control: " + string.Join(" ", control.Select(t => t.Name)));
            Assert.That(fails, Is.Empty);
        }

        // the static fields of Core's hook classes a method stores
        static List<FieldInfo> HookStores(MethodBase method)
        {
            var fields = new List<FieldInfo>();
            foreach ((OpCode op, int token) in PlayerPrepareTests.Operands(method))
            {
                if (op != OpCodes.Stsfld)
                    continue;
                FieldInfo f = null;
                try
                {
                    f = method.Module.ResolveField(token);
                }
                catch (ArgumentException)
                {
                }
                if (f != null && (f.DeclaringType == typeof(CameraHooks) || f.DeclaringType == typeof(LayerHooks)))
                    fields.Add(f);
            }
            return fields;
        }

        // a method's calls of another
        static int Calls(MethodBase method, MethodInfo target)
        {
            int n = 0;
            foreach ((OpCode op, int token) in PlayerPrepareTests.Operands(method))
            {
                if (op.OperandType != OperandType.InlineMethod)
                    continue;
                try
                {
                    if (method.Module.ResolveMethod(token) == target)
                        n++;
                }
                catch (ArgumentException)
                {
                }
            }
            return n;
        }

        sealed class HookUpControl
        {
            internal static void OffTheLoads()
            {
                CameraHooks.TimeDescribe = null;
                FeedbackContext.RegisterExtension(null, null);
                xpTURN.Feedback.Editor.PreviewGlobalState.Register(null);
            }
        }

        // an optional assembly's other hook-ups — every Core hook field it stores is stored at its SubsystemRegistration (its
        // editor's load may store it again) · it registers its extension service there only · the Cinemachine assembly stores the
        // five camera hooks · the editor registers its global states at its load only (the Cinemachine time's among them). The
        // controls: a hook, an extension and a global state outside the loads
        [Test]
        public void FE05b_HookUpsAtTheirLoads()
        {
            const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            MethodInfo extension = typeof(FeedbackContext).GetMethod(nameof(FeedbackContext.RegisterExtension), Static);
            MethodInfo globalState = typeof(xpTURN.Feedback.Editor.PreviewGlobalState).GetMethod(nameof(xpTURN.Feedback.Editor.PreviewGlobalState.Register), Static);
            var fails = new List<string>();
            int extensions = 0;
            foreach (Assembly a in RuntimeAssemblies().Where(a => a != Core))
            {
                string name = a.GetName().Name;
                List<MethodInfo> methods = a.GetTypes().SelectMany(t => t.GetMethods(Static | BindingFlags.DeclaredOnly)).ToList();
                List<MethodInfo> onLoad = methods.Where(m => m.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>()?.loadType == RuntimeInitializeLoadType.SubsystemRegistration).ToList();
                var stored = new HashSet<FieldInfo>(onLoad.SelectMany(HookStores));
                foreach (FieldInfo f in methods.SelectMany(HookStores).Distinct().Where(f => !stored.Contains(f)))
                    fails.Add(name + " stores " + f.DeclaringType.Name + "." + f.Name + " outside its SubsystemRegistration");
                foreach (MethodInfo m in methods.Where(m => Calls(m, extension) > 0))
                {
                    if (onLoad.Contains(m))
                        extensions++;
                    else
                        fails.Add(name + " registers an extension service in " + m.DeclaringType.Name + "." + m.Name + " — not its SubsystemRegistration");
                }
                if (name == CinemachineNames.Assembly)
                {
                    foreach (FieldInfo f in typeof(CameraHooks).GetFields(Static).Where(f => !stored.Contains(f)))
                        fails.Add("CameraHooks." + f.Name + " is not stored at " + name + "'s SubsystemRegistration");
                }
            }
            int expected = (OptionalAssemblyTests.Implemented(typeof(ScreenFlashItem)) ? 1 : 0) + (OptionalAssemblyTests.Implemented(typeof(PostFXBloomItem)) ? 1 : 0)
                + (OptionalAssemblyTests.Implemented(typeof(CinemachineImpulseItem)) ? 1 : 0) + (OptionalAssemblyTests.Implemented(typeof(GamepadRumbleItem)) ? 1 : 0);
            if (extensions != expected)
                fails.Add("extension services at a SubsystemRegistration " + extensions + " (" + expected + " expected)");
            List<MethodInfo> registering = typeof(xpTURN.Feedback.Editor.PreviewGlobalState).Assembly.GetTypes().SelectMany(t => t.GetMethods(Static | BindingFlags.DeclaredOnly))
                .Where(m => Calls(m, globalState) > 0).ToList();
            foreach (MethodInfo m in registering.Where(m => m.GetCustomAttribute<UnityEditor.InitializeOnLoadMethodAttribute>() == null))
                fails.Add(m.DeclaringType.Name + "." + m.Name + " registers a global state outside the editor's load");
            if (!registering.Any(m => m.DeclaringType == typeof(xpTURN.Feedback.Editor.CinemachineTimeState)))
                fails.Add("the Cinemachine time is registered nowhere");
            MethodInfo control = typeof(HookUpControl).GetMethod(nameof(HookUpControl.OffTheLoads), Static);
            string seen = string.Join(" ", HookStores(control).Select(f => f.Name)) + " · " + Calls(control, extension) + " · " + Calls(control, globalState);
            if (seen != "TimeDescribe · 1 · 1")
                fails.Add("the control: " + seen);
            Assert.That(fails, Is.Empty);
        }

        // the package runtime reads no input — no member of the legacy UnityEngine.Input called, loaded or named
        static List<string> LegacyInput(IEnumerable<Assembly> assemblies)
        {
            var hits = new List<string>();
            const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            foreach (Assembly a in assemblies)
            {
                foreach (Type type in a.GetTypes())
                {
                    Type[] typeArgs = type.IsGenericType ? type.GetGenericArguments() : null;
                    foreach (MethodBase method in type.GetMethods(All).Cast<MethodBase>().Concat(type.GetConstructors(All)))
                    {
                        Type[] methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;
                        foreach ((OpCode op, int token) in PlayerPrepareTests.Operands(method))
                        {
                            MemberInfo member = null;
                            try
                            {
                                if (op.OperandType == OperandType.InlineMethod)
                                    member = method.Module.ResolveMethod(token, typeArgs, methodArgs);
                                else if (op.OperandType == OperandType.InlineType)
                                    member = method.Module.ResolveType(token, typeArgs, methodArgs);
                                else if (op.OperandType == OperandType.InlineField)
                                    member = method.Module.ResolveField(token, typeArgs, methodArgs);
                            }
                            catch (ArgumentException)
                            {
                            }
                            if (member != null && (member == typeof(Input) || member.DeclaringType == typeof(Input)))
                                hits.Add(type.Name + "." + method.Name + " → Input." + member.Name);
                        }
                    }
                }
            }
            return hits;
        }

        sealed class LegacyControl
        {
            internal static bool Read() => Input.GetKey(KeyCode.Space);
        }

        [Test]
        public void FE08_Reg05_RuntimeReadsNoLegacyInput()
        {
            var fails = new List<string>(LegacyInput(RuntimeAssemblies()));
            List<string> control = LegacyInput(new[] { typeof(LegacyControl).Assembly }).Where(h => h.StartsWith(nameof(LegacyControl) + ".", StringComparison.Ordinal)).ToList();
            if (control.Count != 1)
                fails.Add("the control: " + string.Join(" | ", control));
            Assert.That(fails, Is.Empty);
        }
    }
}
