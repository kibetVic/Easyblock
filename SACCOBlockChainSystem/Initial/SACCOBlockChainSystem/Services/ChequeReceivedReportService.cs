using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Models;
using SACCOBlockChainSystem.Models.ViewModels;

namespace SACCOBlockChainSystem.Services
{
    public interface IChequeReceivedReportService
    {
        Task<ChequeReceivedReportViewModel> BuildReportAsync(ChequeReceivedReportFilter filter, string companyCode);
    }

    public class ChequeReceivedReportService : IChequeReceivedReportService
    {
        private readonly ApplicationDbContext _db;

        public ChequeReceivedReportService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<ChequeReceivedReportViewModel> BuildReportAsync(ChequeReceivedReportFilter filter, string companyCode)
        {
            IQueryable<Cheque> query = _db.Cheques.AsNoTracking();

            // Filter by company code
            if (!string.IsNullOrEmpty(companyCode))
                query = query.Where(c => c.CompanyCode == companyCode);

            // Date range filter
            if (filter.DateFrom.HasValue)
                query = query.Where(c => c.DateIssued >= filter.DateFrom.Value);
            if (filter.DateTo.HasValue)
            {
                var endDate = filter.DateTo.Value.Date.AddDays(1);
                query = query.Where(c => c.DateIssued < endDate);
            }

            // Fetch records with explicit sorting: by SACCO, then by DateIssued descending (newest first)
            var records = await query
                .OrderBy(c => c.CompanyCode)
                .ThenByDescending(c => c.DateIssued)
                .Select(c => new ChequeReceivedRecord
                {
                    ReceiptNumber = c.TransactionNo ?? c.Voucherno,
                    MemberNumber = c.MemberNo,
                    ChequeNumber = c.ChequeNo,
                    Amount = c.Amount ?? 0,
                    DateDeposited = c.DateIssued,
                    SaccoName = c.CompanyCode ?? "Unknown SACCO"
                })
                .ToListAsync();

            // Group by SACCO name and sort records within each group (newest first)
            var groups = records
                .GroupBy(r => r.SaccoName)
                .Select(g => new ChequeSaccoGroup
                {
                    SaccoName = g.Key,
                    Records = g.OrderByDescending(r => r.DateDeposited).ThenBy(r => r.ReceiptNumber).ToList(),
                    Subtotal = g.Sum(r => r.Amount)
                })
                .OrderBy(g => g.SaccoName)
                .ToList();

            var grandTotal = groups.Sum(g => g.Subtotal);

            return new ChequeReceivedReportViewModel
            {
                Filter = filter,
                Groups = groups,
                GrandTotal = grandTotal,
                TotalRecords = records.Count,
                GeneratedAt = DateTime.Now
            };
        }
    }
}