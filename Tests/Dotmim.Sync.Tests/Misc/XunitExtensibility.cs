using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;
using Xunit.v3;

namespace Wormhole.Sync.Tests.Misc
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class TestPriorityAttribute : Attribute
    {
        public TestPriorityAttribute(int priority)
        {
            Priority = priority;
        }

        public int Priority { get; private set; }
    }

    public class PriorityOrderer : ITestCaseOrderer
    {
        IReadOnlyCollection<TTestCase> ITestCaseOrderer.OrderTestCases<TTestCase>(IReadOnlyCollection<TTestCase> testCases)
        {
            var sortedMethods = new SortedDictionary<int, List<TTestCase>>();

            foreach (var testCase in testCases)
            {
                int priority = 0;

                // get the attribute priority
                var testMethod = testCase.GetType().GetProperty("TestMethod")?.GetValue(testCase);
                var method = testMethod?.GetType().GetProperty("Method")?.GetValue(testMethod);
                var getCustomAttributes = method?.GetType().GetMethod("GetCustomAttributes", new[] { typeof(string) });
                var attrs = getCustomAttributes?.Invoke(method, new object[] { typeof(TestPriorityAttribute).AssemblyQualifiedName }) as System.Collections.IEnumerable;

                if (attrs != null)
                {
                    foreach (var attr in attrs)
                    {
                        var getNamedArgument = attr.GetType().GetMethod("GetNamedArgument");
                        if (getNamedArgument != null)
                        {
                            var genericMethod = getNamedArgument.MakeGenericMethod(typeof(int));
                            priority = (int)genericMethod.Invoke(attr, new object[] { "Priority" });
                        }
                    }
                }

                // get the all the tests marked with this priority
                // we could potentially have multiple tests with same priority
                sortedMethods.TryGetValue(priority, out var lstTestsForPriority);

                // if new priority with no test for this priority, just add it to my sorted list
                if (lstTestsForPriority == null)
                {
                    lstTestsForPriority = new List<TTestCase>();
                    sortedMethods.Add(priority, lstTestsForPriority);
                }

                // add the test case to the list, already part of the sortedMethods list
                lstTestsForPriority.Add(testCase);
            }

            var result = new List<TTestCase>();
            foreach (var list in sortedMethods.Keys.Select(priority => sortedMethods[priority]))
            {
                // potentially we could have multiple tests with same priority
                // sort ordered by name - use reflection to get method name
                list.Sort((x, y) => {
                    var xTestMethod = x.GetType().GetProperty("TestMethod")?.GetValue(x);
                    var xMethod = xTestMethod?.GetType().GetProperty("Method")?.GetValue(xTestMethod);
                    var xName = xMethod?.GetType().GetProperty("Name")?.GetValue(xMethod) as string ?? "";

                    var yTestMethod = y.GetType().GetProperty("TestMethod")?.GetValue(y);
                    var yMethod = yTestMethod?.GetType().GetProperty("Method")?.GetValue(yTestMethod);
                    var yName = yMethod?.GetType().GetProperty("Name")?.GetValue(yMethod) as string ?? "";

                    return StringComparer.OrdinalIgnoreCase.Compare(xName, yName);
                });
                result.AddRange(list);
            }

            return result;
        }

    }
}
