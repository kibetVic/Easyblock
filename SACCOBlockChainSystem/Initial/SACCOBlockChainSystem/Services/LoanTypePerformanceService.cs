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
    public interface ILoanTypePerformanceService
    {
        Task<LoanTypePerformanceViewModel> BuildReportAsync(LoanTypePerformanceFilter filter, string? companyCode);
    }

    public class LoanTypePerformanceService : ILoanTypePerformanceService
    {
        private readonly ApplicationDbContext _db;

        public LoanTypePerformanceService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<LoanTypePerformanceViewModel> BuildReportAsync(LoanTypePerformanceFilter filter, string? companyCode)
        {
            var asAtDate = filter.AsAtDate ?? DateTime.Today;

            if (string.IsNullOrEmpty(companyCode))
            {
                return new LoanTypePerformanceViewModel
                {
                    Filter = filter,
                    Records = new List<LoanTypePerformanceRecord>(),
                    GrandTotalDisbursed = 0,
                    GrandTotalPrincipalBalance = 0,
                    GrandTotalArrears = 0,
                    OverallPAR = 0,
                    TotalRecords = 0,
                    GeneratedAt = DateTime.Now
                };
            }

            var loanData = await _db.Loans
                .Where(l => l.AuditDateTime <= asAtDate && l.CompanyCode == companyCode)
                .Select(l => new { l.LoanNo, l.LoanCode, l.LoanAmt })
                .ToListAsync();

            var loanTypes = await _db.Loantypes
                .Where(lt => lt.CompanyCode == companyCode)
                .Select(lt => new { lt.LoanCode, lt.LoanType1, lt.LoanProduct })
                .ToListAsync();

            var joined = loanData
                .Join(loanTypes,
                    loan => loan.LoanCode,
                    loanType => loanType.LoanCode,
                    (loan, loanType) => new { loan, loanType })
                .ToList();

            var latestBalances = await _db.Loanbal
                .GroupBy(b => b.LoanNo)
                .Select(g => new
                {
                    LoanNo = g.Key,
                    Balance = g.OrderByDescending(b => b.Id).FirstOrDefault().Balance
                })
                .ToDictionaryAsync(b => b.LoanNo, b => b.Balance);

            var records = joined
                .GroupBy(x => x.loanType.LoanType1 ?? x.loanType.LoanProduct ?? "Unknown")
                .Select(g => new LoanTypePerformanceRecord
                {
                    LoanTypeName = g.Key,
                    TotalDisbursed = g.Sum(x => x.loan.LoanAmt ?? 0),
                    TotalPrincipalBalance = g.Sum(x => latestBalances.ContainsKey(x.loan.LoanNo) ? latestBalances[x.loan.LoanNo] : 0),
                    TotalArrears = 0
                })
                .OrderBy(r => r.LoanTypeName)
                .ToList();

            foreach (var record in records)
            {
                record.PAR = record.TotalPrincipalBalance > 0
                    ? Math.Round((record.TotalArrears / record.TotalPrincipalBalance) * 100, 2)
                    : 0;
            }

            return new LoanTypePerformanceViewModel
            {
                Filter = filter,
                Records = records,
                GrandTotalDisbursed = records.Sum(r => r.TotalDisbursed),
                GrandTotalPrincipalBalance = records.Sum(r => r.TotalPrincipalBalance),
                GrandTotalArrears = records.Sum(r => r.TotalArrears),
                OverallPAR = records.Sum(r => r.TotalPrincipalBalance) > 0
                    ? Math.Round((records.Sum(r => r.TotalArrears) / records.Sum(r => r.TotalPrincipalBalance)) * 100, 2)
                    : 0,
                TotalRecords = records.Count,
                GeneratedAt = DateTime.Now
            };
        }
    }
}