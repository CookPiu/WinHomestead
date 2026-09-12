using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using WinHomestead.Core.Models;

namespace WinHomestead.Core.Infrastructure;

/// <summary>把执行结果渲染为单文件 HTML（内联样式，无外部资源）。</summary>
public static class ReportHtml
{
    public static string Render(EnvironmentSnapshot? snapshot, Plan? plan, ExecutionResult result, IReadOnlyList<string> manualSteps)
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"" + L.S("zh-CN", "en") + "\"><head><meta charset=\"utf-8\"><title>"
                  + L.S("开荒报告", "WinHomestead report") + "</title><style>")
          .Append("body{font-family:'Segoe UI Variable','Microsoft YaHei',sans-serif;max-width:900px;margin:32px auto;padding:0 16px;color:#1b1b1b}")
          .Append("h1{font-size:22px}h2{font-size:16px;margin-top:28px}table{border-collapse:collapse;width:100%}")
          .Append("td,th{border-bottom:1px solid #e5e5e5;padding:6px 8px;text-align:left;vertical-align:top;font-size:13px}")
          .Append(".ok{color:#0f7b0f}.bad{color:#c42b1c}.muted{color:#6b6b6b}li{margin:6px 0}")
          .Append("</style></head><body>");

        sb.Append("<h1>").Append(L.S("开荒报告", "WinHomestead report")).Append("</h1>");
        sb.Append("<p class=\"muted\">").Append(L.S("方案 ", "Plan ")).Append(E(result.PlanId))
          .Append(L.S(" · 开始 ", " · started ")).Append(result.StartedAt.ToString("yyyy-MM-dd HH:mm:ss"))
          .Append(result.FinishedAt.HasValue ? L.S(" · 结束 ", " · finished ") + result.FinishedAt.Value.ToString("HH:mm:ss") : string.Empty)
          .Append(result.Aborted ? L.S(" · <b>已中止</b>", " · <b>aborted</b>") : string.Empty)
          .Append("</p>");

        if (snapshot != null)
        {
            sb.Append("<h2>").Append(L.S("电脑", "This PC")).Append("</h2><table>");
            Row(sb, L.S("系统", "System"), L.S($"{snapshot.OsCaption}（内部版本 {snapshot.Build}）", $"{snapshot.OsCaption} (build {snapshot.Build})"));
            Row(sb, L.S("机型", "Model"), L.S(
                $"{snapshot.Manufacturer} {snapshot.Model} · {(snapshot.IsLaptop ? "笔记本" : "台式机")} · 内存 {snapshot.RamGb:F2} GB",
                $"{snapshot.Manufacturer} {snapshot.Model} · {(snapshot.IsLaptop ? "laptop" : "desktop")} · {snapshot.RamGb:F2} GB RAM"));
            var vols = new List<string>();
            foreach (var v in snapshot.Volumes)
                vols.Add(L.S($"{v.DriveLetter} {v.SizeGb:F2} GB（剩 {v.FreeGb:F2} GB）", $"{v.DriveLetter} {v.SizeGb:F2} GB ({v.FreeGb:F2} GB free)"));
            Row(sb, L.S("分区", "Volumes"), string.Join(L.S("；", "; "), vols));
            Row(sb, L.S("数据盘", "Data drive"), plan?.Answers.DataDrive ?? snapshot.DataDrive ?? L.S("无", "none"));
            sb.Append("</table>");
        }

        var ok = result.Count(TaskOutcome.Done) + result.Count(TaskOutcome.NeedsReboot);
        var failed = result.Count(TaskOutcome.Failed) + result.Count(TaskOutcome.RolledBack);
        sb.Append("<h2>").Append(L.S("执行结果", "Results")).Append("</h2><p>")
          .Append(L.S($"成功 {ok} 项 · 失败 {failed} 项", $"{ok} succeeded · {failed} failed"))
          .Append(result.RebootRequired ? L.S(" · 需要重启", " · reboot required") : string.Empty).Append("</p>");
        sb.Append("<table><tr><th>").Append(L.S("任务", "Item")).Append("</th><th>").Append(L.S("结果", "Outcome"))
          .Append("</th><th>").Append(L.S("说明", "Detail")).Append("</th></tr>");
        foreach (var r in result.Results)
        {
            var good = r.Outcome is TaskOutcome.Done or TaskOutcome.NeedsReboot or TaskOutcome.Skipped;
            sb.Append("<tr><td>").Append(E(r.DisplayName)).Append("</td><td class=\"").Append(good ? "ok" : "bad").Append("\">")
              .Append(E(OutcomeLabel(r.Outcome))).Append("</td><td class=\"muted\">").Append(E(r.Message ?? string.Empty)).Append("</td></tr>");
        }
        sb.Append("</table>");

        if (plan != null)
        {
            var skipped = new List<PlanItem>();
            foreach (var i in plan.Items) if (i.State == PlanState.Skipped) skipped.Add(i);
            if (skipped.Count > 0)
            {
                sb.Append("<h2>").Append(L.S("已满足、未改动", "Already satisfied, untouched")).Append("</h2><ul>");
                foreach (var i in skipped) sb.Append("<li>").Append(E(i.DisplayName)).Append("</li>");
                sb.Append("</ul>");
            }
        }

        if (manualSteps.Count > 0)
        {
            sb.Append("<h2>").Append(L.S("需要手动处理", "Needs your attention")).Append("</h2><ol>");
            foreach (var s in manualSteps) sb.Append("<li>").Append(E(s)).Append("</li>");
            sb.Append("</ol>");
        }

        sb.Append("<p class=\"muted\">").Append(L.S("生成于 ", "Generated ")).Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).Append("</p></body></html>");
        return sb.ToString();
    }

    public static string OutcomeLabel(TaskOutcome o) => o switch
    {
        TaskOutcome.Done => L.S("完成", "done"),
        TaskOutcome.NeedsReboot => L.S("完成（需重启）", "done (reboot required)"),
        TaskOutcome.Skipped => L.S("跳过", "skipped"),
        TaskOutcome.RolledBack => L.S("失败，已回滚", "failed, rolled back"),
        TaskOutcome.Failed => L.S("失败", "failed"),
        TaskOutcome.Aborted => L.S("已中止", "aborted"),
        _ => o.ToString(),
    };

    private static void Row(StringBuilder sb, string k, string v)
        => sb.Append("<tr><td class=\"muted\" style=\"width:90px\">").Append(E(k)).Append("</td><td>").Append(E(v)).Append("</td></tr>");

    private static string E(string s) => WebUtility.HtmlEncode(s);
}
