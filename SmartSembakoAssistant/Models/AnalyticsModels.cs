using System.Collections.Generic;

namespace SmartSembakoAssistant.Models
{
    public class WeeklySalesComparison
    {
        public decimal ThisWeekRevenue { get; set; }
        public decimal LastWeekRevenue { get; set; }
        public decimal ThisWeekProfit { get; set; }
        public decimal LastWeekProfit { get; set; }
        public decimal GrowthPct { get; set; }
        public string TrendLabel { get; set; } = "STABIL";
        public string TrendIcon { get; set; } = "->";
        public int ThisWeekTxCount { get; set; }
        public int LastWeekTxCount { get; set; }
        public List<ProductSalesData> TopProducts { get; set; } = new();
    }

    public class MonthlySalesTrend
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public string Label { get; set; } = "";
        public decimal Revenue { get; set; }
        public decimal Profit { get; set; }
        public int TxCount { get; set; }
        public decimal GrowthVsPrevMonth { get; set; }
        public bool HasGrowthBaseline { get; set; }
    }

    public class HourlySalesData
    {
        public int Hour { get; set; }
        public string HourLabel { get; set; } = "";
        public decimal Revenue { get; set; }
        public int TxCount { get; set; }
    }

    public class TrendAnalysisPromptData
    {
        public decimal WeeklyRevenue { get; set; }
        public decimal WeeklyGrowthPct { get; set; }
        public string WeeklyTrendLabel { get; set; } = "";
        public decimal MonthlyRevenue { get; set; }
        public decimal MonthlyGrowthPct { get; set; }
        public bool MonthlyHasGrowthBaseline { get; set; }
        public string? TopProductName { get; set; }
        public decimal TopProductRevenue { get; set; }
        public string? AnomalyNote { get; set; }
    }
}
