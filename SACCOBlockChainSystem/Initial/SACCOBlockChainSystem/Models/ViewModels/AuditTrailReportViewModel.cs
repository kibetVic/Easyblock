// Models/ViewModels/AuditTrailReportViewModel.cs
using System;
using System.Collections.Generic;

namespace SACCOBlockChainSystem.Models.ViewModels
{
	public class AuditTrailReportViewModel
	{
		public DateTime? StartDate { get; set; }
		public DateTime? EndDate { get; set; }
		public List<UserBlock> UserBlocks { get; set; } = new List<UserBlock>();
		public int TotalRecords { get; set; }
		public int UniqueUsers { get; set; }
		public DateTime ReportGeneratedDate { get; set; } = DateTime.Now;
        public object CompanyCode { get; internal set; }
    }

	public class UserBlock
	{
		public string UserName { get; set; } = string.Empty;
		public string Location { get; set; } = string.Empty;
		public string IpAddress { get; set; } = string.Empty;
		public string HostName { get; set; } = string.Empty;
		public List<TransactionItem> Txns { get; set; } = new List<TransactionItem>();
	}

	public class TransactionItem
	{
		public DateTime? TransactionDate { get; set; }
		public decimal? Amount { get; set; }
		public DateTime? AuditTime { get; set; }
		public string ActionDescription { get; set; } = string.Empty;
		public string TableName { get; set; } = string.Empty;
	}
}