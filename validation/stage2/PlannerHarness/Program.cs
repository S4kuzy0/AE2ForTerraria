using System;
using System.Diagnostics;
using AE2ForTerraria.Systems;
int passed = 0, failed = 0;
var timer = Stopwatch.StartNew();
PlannerRegression.Run((ok, name) =>
{
    if (ok) passed++; else failed++;
    Console.WriteLine((ok ? "PASS " : "FAIL ") + name);
});
Console.WriteLine($"SUMMARY passed={passed} failed={failed} elapsed_ms={timer.Elapsed.TotalMilliseconds:F2}");
return failed == 0 ? 0 : 1;
