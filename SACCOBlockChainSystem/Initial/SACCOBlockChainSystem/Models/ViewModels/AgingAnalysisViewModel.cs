using System;
using System.Collections.Generic;

namespace SACCOBlockChainSystem.Models.ViewModels
{
	public class AgingAnalysisViewModel
	{
		public string LoanNo { get; set; }
		public string MemberNo { get; set; }
		public string FullName { get; set; }
		public decimal LoanBalance { get; set; }
		public int? RepayPeriod { get; set; }  // in months
		public DateTime? NextDueDate { get; set; }
		public DateTime? DateIssued { get; set; }
		public int DaysInArrears { get; set; }
		public DateTime? LastRepayDate { get; set; }
		public DateTime? DateOfCompletion { get; set; }
		public string ValueChain { get; set; }

		// Cumulative arrears amounts
		public decimal Performing { get; set; }        // Current month due
		public decimal SpecialMention { get; set; }    // 2 months due (current + 1 previous)
		public decimal Watchful { get; set; }          // 3 months due
		public decimal Substandard { get; set; }       // 4 months due
		public decimal Doubtful { get; set; }          // 6 months due
		public decimal Loss { get; set; }              // 12 months due
		public decimal LossOver365 { get; set; }       // All months due + penalty

		public string Classification { get; set; }
		public int ArrearsCategory { get; set; }

		// Additional properties for calculation
		public decimal MonthlyInstallment { get; set; }  // The principal + interest due each month
		public int MonthsInArrears { get; set; }         // Number of months in arrears
	}

	public class AgingAnalysisIndexViewModel
	{
		public List<AgingAnalysisViewModel> Loans { get; set; } = new List<AgingAnalysisViewModel>();

		// Totals
		public decimal TotalLoanBalance { get; set; }
		public decimal TotalPerforming { get; set; }
		public decimal TotalSpecialMention { get; set; }
		public decimal TotalWatchful { get; set; }
		public decimal TotalSubstandard { get; set; }
		public decimal TotalDoubtful { get; set; }
		public decimal TotalLoss { get; set; }
		public decimal TotalLossOver365 { get; set; }

		// Statistics
		public int TotalLoans { get; set; }
		public int PerformingCount { get; set; }
		public int SpecialMentionCount { get; set; }
		public int WatchfulCount { get; set; }
		public int SubstandardCount { get; set; }
		public int DoubtfulCount { get; set; }
		public int LossCount { get; set; }
		public int LossOver365Count { get; set; }

		// Report Information
		public DateTime ReportDate { get; set; }
		public DateTime AsAtDate { get; set; }
		public bool HasData { get; set; }
		public string UserCompanyCode { get; set; }
		public string CompanyName { get; set; }
	}

	public static class AgingCategories
	{
		public const int PERFORMING = 0;           // 0 days
		public const int SPECIAL_MENTION = 1;      // 1-30 days
		public const int WATCHFUL = 2;              // 31-60 days
		public const int SUBSTANDARD = 3;           // 61-90 days
		public const int DOUBTFUL = 4;              // 91-180 days
		public const int LOSS = 5;                   // 181-365 days
		public const int LOSS_OVER_365 = 6;          // Over 365 days

		public static string GetCategoryName(int category)
		{
			return category switch
			{
				PERFORMING => "Performing",
				SPECIAL_MENTION => "Special Mention",
				WATCHFUL => "Watchful",
				SUBSTANDARD => "Substandard",
				DOUBTFUL => "Doubtful",
				LOSS => "Loss",
				LOSS_OVER_365 => "Loss Over 365",
				_ => "Unknown"
			};
		}

		public static string GetCategoryDisplay(int category)
		{
			return category switch
			{
				PERFORMING => "Performing 0 Days",
				SPECIAL_MENTION => "Special Mention 1-30 Days",
				WATCHFUL => "Watchful 31-60 Days",
				SUBSTANDARD => "Substandard 61-90 Days",
				DOUBTFUL => "Doubtful 91-180 Days",
				LOSS => "Loss 181-365 Days",
				LOSS_OVER_365 => "Loss Over 365 Days",
				_ => "Unknown"
			};
		}

		public static (int MinDays, int MaxDays) GetCategoryRange(int category)
		{
			return category switch
			{
				PERFORMING => (0, 0),
				SPECIAL_MENTION => (1, 30),
				WATCHFUL => (31, 60),
				SUBSTANDARD => (61, 90),
				DOUBTFUL => (91, 180),
				LOSS => (181, 365),
				LOSS_OVER_365 => (366, int.MaxValue),
				_ => (0, 0)
			};
		}

		public static int GetCategoryFromDays(int days)
		{
			if (days <= 0) return PERFORMING;
			if (days <= 30) return SPECIAL_MENTION;
			if (days <= 60) return WATCHFUL;
			if (days <= 90) return SUBSTANDARD;
			if (days <= 180) return DOUBTFUL;
			if (days <= 365) return LOSS;
			return LOSS_OVER_365;
		}
	}
}