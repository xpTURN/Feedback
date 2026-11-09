using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace xpTURN.Feedback.Tests
{
    // the dotnet layer's regression cases against its own test names — the Unity layer checks its own. The table holds
    // the 25 numbers in scope; this layer holds #2 · #3 · #24
    public sealed class RegressionListTests
    {
        static readonly int[] s_InScope = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 24, 25, 26, 27 };
        static readonly int[] s_Here = { 2, 3, 24 };
        static readonly Regex s_Number = new Regex(@"Reg(\d{2})(?!\d)", RegexOptions.CultureInvariant);

        static IEnumerable<string> TestNames(Assembly assembly)
            => assembly.GetTypes().SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                .Where(m => m.IsDefined(typeof(TestAttribute), false) || m.IsDefined(typeof(TestCaseAttribute), false) || m.IsDefined(typeof(TestCaseSourceAttribute), false))
                .Select(m => m.Name).Distinct();

        static List<string> Problems(IEnumerable<string> names, int[] inScope, int[] here)
        {
            var found = new HashSet<int>();
            var problems = new List<string>();
            foreach (string name in names)
            {
                foreach (Match m in s_Number.Matches(name))
                {
                    int n = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                    found.Add(n);
                    if (Array.IndexOf(inScope, n) < 0)
                        problems.Add(name + " names #" + n + ", which is not in the table");
                }
            }
            problems.AddRange(here.Where(n => !found.Contains(n)).Select(n => "#" + n + " has no test"));
            return problems;
        }

        [Test]
        public void FD01_InScopeDefectsHaveTheirTests()
        {
            var fails = Problems(TestNames(typeof(RegressionListTests).Assembly), s_InScope, s_Here);
            List<string> control = Problems(new[] { "Reg02_A", "Reg22_Out" }, s_InScope, new[] { 2, 3 });
            if (control.Count != 2)
                fails.Add("the control: " + string.Join(" | ", control));
            Assert.That(fails, Is.Empty);
        }
    }
}
