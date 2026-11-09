using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.TestTools;

namespace xpTURN.Feedback.Tests.Editor
{
    // the regression cases in scope against the test names. A number in a test's name says that test is the case's
    // regression (RegNN — two digits, two cases joined). Every number of this layer must have a test; a number outside
    // the table is a typo. The dotnet layer checks its own
    public sealed class RegressionListTests
    {
        enum Layer
        {
            Unity,
            Dotnet,
            Later,      // the item's assembly is not compiled here — its test is not either
        }

        // #16 (TMP softness) and #18 (a floating text's scale start) are in the UI test assembly, compiled where uGUI is
        // (the minimal project has neither)
        static readonly IReadOnlyDictionary<int, Layer> InScope = new Dictionary<int, Layer>
        {
            { 1, Layer.Unity }, { 2, Layer.Dotnet }, { 3, Layer.Dotnet }, { 4, Layer.Unity }, { 5, Layer.Unity }, { 6, Layer.Unity },
            { 7, Layer.Unity }, { 8, Layer.Unity }, { 9, Layer.Unity }, { 10, Layer.Unity }, { 11, Layer.Unity }, { 12, Layer.Unity },
            { 13, Layer.Unity }, { 14, Layer.Unity }, { 15, Layer.Unity }, { 16, OptionalAssemblyTests.Implemented(typeof(TMPSoftnessItem)) ? Layer.Unity : Layer.Later },
            { 17, Layer.Unity }, { 18, OptionalAssemblyTests.Implemented(typeof(FloatingTextItem)) ? Layer.Unity : Layer.Later },
            { 19, Layer.Unity }, { 20, Layer.Unity }, { 21, Layer.Unity }, { 24, Layer.Dotnet }, { 25, Layer.Unity }, { 26, Layer.Unity },
            { 27, Layer.Unity },
        };

        static readonly Regex s_Number = new Regex(@"Reg(\d{2})(?!\d)", RegexOptions.CultureInvariant);

        // the test methods the editor has loaded — the runtime test assembly is compiled for the editor too · [TestCase]
        // alone makes a test without [Test]
        static IEnumerable<string> TestNames()
            => TypeCache.GetMethodsWithAttribute<TestAttribute>()
                .Concat(TypeCache.GetMethodsWithAttribute<UnityTestAttribute>())
                .Concat(TypeCache.GetMethodsWithAttribute<TestCaseAttribute>())
                .Concat(TypeCache.GetMethodsWithAttribute<TestCaseSourceAttribute>())
                .Where(m => m.DeclaringType != null && m.DeclaringType.Assembly.GetName().Name.StartsWith("xpTURN.Feedback", StringComparison.Ordinal))
                .Select(m => m.Name).Distinct();

        static List<string> Problems(IEnumerable<string> names, IReadOnlyDictionary<int, Layer> table, Layer layer)
        {
            var found = new HashSet<int>();
            var problems = new List<string>();
            foreach (string name in names)
            {
                foreach (Match m in s_Number.Matches(name))
                {
                    int n = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                    found.Add(n);
                    if (!table.ContainsKey(n))
                        problems.Add(name + " names #" + n + ", which is not in the table");
                }
            }
            foreach (KeyValuePair<int, Layer> entry in table)
            {
                if (entry.Value == layer && !found.Contains(entry.Key))
                    problems.Add("#" + entry.Key + " has no test named Reg" + entry.Key.ToString("00", CultureInfo.InvariantCulture));
            }
            return problems;
        }

        [Test]
        public void FE01_InScopeDefectsHaveTheirTests()
        {
            var fails = new List<string>(Problems(TestNames(), InScope, Layer.Unity));
            // the same check over a list with one number missing and one outside the table
            var table = new Dictionary<int, Layer> { { 1, Layer.Unity }, { 4, Layer.Unity }, { 15, Layer.Later } };
            List<string> control = Problems(new[] { "VE01_Reg01_ChannelKinds", "Reg22_Outside" }, table, Layer.Unity);
            if (control.Count != 2 || !control.Any(p => p.Contains("#22")) || !control.Any(p => p.StartsWith("#4 ", StringComparison.Ordinal)))
                fails.Add("the control: " + string.Join(" | ", control));
            Assert.That(fails, Is.Empty);
        }
    }
}
