using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using NewPcSetup.Core.Models;

namespace NewPcSetup.Core.Infrastructure;

/// <summary>把执行结果渲染为单文件 HTML（内联样式，无外部资源）。</summary>
public static class ReportHtml
{
    public static string Render(EnvironmentSnapshot? snapshot, Plan? plan, ExecutionResult result, IReadOnlyList<string> manualSteps)
    {
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\"><title>新机开荒报告</title><style>")
          .Append("body{font-family:'Segoe UI Variable','Microsoft YaHei',sans-serif;max-width:900px;margin:32px auto;padding:0 16px;color:#1b1b1b}")
          .Append("h1{font-size:22px}h2{font-size:16px;margin-top:28px}table{border-collapse:collapse;width:100%}")
          .Append("td,th{border-bottom:1px solid #e5e5e5;padding:6px 8px;text-align:left;vertical-align:top;font-size:13px}")
          .Append(".ok{color:#0f7b0f}.bad{color:#c42b1c}.muted{color:#6b6b6b}li{margin:6px 0}")
          .Append("</style></head><body>");

        sb.Append("<h1>新机开荒报告</h1>");
        sb.Append("<p class=\"muted\">方案 ").Append(E(result.PlanId))
          .Append(" · 开始 ").Append(result.StartedAt.ToString("yyyy-MM-dd HH:mm:ss"))
          .Append(result.FinishedAt.HasValue ? " · 结束 " + result.FinishedAt.Value.ToString("HH:mm:ss") : string.Empty)
          .Append(result.Aborted ? " · <b>已中止</b>" : string.Empty)
          .Append("</p>");

        if (snapshot != null)
        {
            sb.Append("<h2>电脑</h2><table>");
            Row(sb, "系统", $"{snapshot.OsCaption}（内部版本 {snapshot.Build}）");
            Row(sb, "机型", $"{snapshot.Manufacturer} {snapshot.Model} · {(snapshot.IsLaptop ? "笔记本" : "台式机")} · {snapshot.RamGb} GB");
            var vols = new List<string>();
            foreach (var v in snapshot.Volumes) vols.Add($"{v.DriveLetter} {v.SizeGb:F0} GB（剩 {v.FreeGb:F0} GB）");
            Row(sb, "分区", string.Join("；", vols));
            Row(sb, "数据盘", plan?.Answers.DataDrive ?? snapshot.DataDrive ?? "无");
            sb.Append("</table>");
        }

        var ok = result.Count(TaskOutcome.Done) + result.Count(TaskOutcome.NeedsReboot);
        var failed = result.Count(TaskOutcome.Failed) + result.Count(TaskOutcome.RolledBack);
        sb.Append("<h2>执行结果</h2><p>成功 ").Append(ok).Append(" 项 · 失败 ").Append(failed).Append(" 项")
          .Append(result.RebootRequired ? " · 需要重启" : string.Empty).Append("</p>");
        sb.Append("<table><tr><th>任务</th><th>结果</th><th>说明</th></tr>");
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
                sb.Append("<h2>已满足、未改动</h2><ul>");
                foreach (var i in skipped) sb.Append("<li>").Append(E(i.DisplayName)).Append("</li>");
                sb.Append("</ul>");
            }
        }

        if (manualSteps.Count > 0)
        {
            sb.Append("<h2>需要手动处理</h2><ol>");
            foreach (var s in manualSteps) sb.Append("<li>").Append(E(s)).Append("</li>");
            sb.Append("</ol>");
        }

        sb.Append("<p class=\"muted\">生成于 ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).Append("</p></body></html>");
        return sb.ToString();
    }

    public static string OutcomeLabel(TaskOutcome o) => o switch
    {
        TaskOutcome.Done => "完成",
        TaskOutcome.NeedsReboot => "完成（需重启）",
        TaskOutcome.Skipped => "跳过",
        TaskOutcome.RolledBack => "失败，已回滚",
        TaskOutcome.Failed => "失败",
        TaskOutcome.Aborted => "已中止",
        _ => o.ToString(),
    };

    private static void Row(StringBuilder sb, string k, string v)
        => sb.Append("<tr><td class=\"muted\" style=\"width:90px\">").Append(E(k)).Append("</td><td>").Append(E(v)).Append("</td></tr>");

    private static string E(string s) => WebUtility.HtmlEncode(s);
}
