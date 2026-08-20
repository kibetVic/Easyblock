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
    public interface IPortfolioAtRiskService
    {
        Task<PortfolioAtRiskViewModel> BuildReportAsync(PortfolioAtRiskFilter filter);
    }

    public class PortfolioAtRiskService : IPortfolioAtRiskService
    {
        private readonly ApplicationDbContext _db;

        public PortfolioAtRiskService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<PortfolioAtRiskViewModel> BuildReportAsync(PortfolioAtRiskFilter filter)
        {
            try
            {
                var asAtDate = filter.AsAtDate ?? DateTime.Today;

                // Get all loans with their basic info first (avoid complex joins)
                var loansList = await _db.Loans
                    .Where(l => l.AuditDateTime <= asAtDate)
                    .Select(l => new
                    {
                        l.LoanNo,
                        l.LoanCode,
                        l.CompanyCode
                    })
                    .ToListAsync();

                if (!loansList.Any())
                {
                    return GetEmptyViewModel(filter);
                }

                // Get loan types separately
                var loanTypesList = await _db.Loantypes
                    .Select(lt => new
                    {
                        lt.LoanCode,
                        lt.LoanType1,
                        lt.LoanProduct
                    })
                    .ToListAsync();

                // Get latest balances - SAFE approach without boolean casting
                var latestBalancesDict = new Dictionary<string, decimal>();

                var allBalances = await _db.Loanbal
                    .Select(b => new { b.LoanNo, b.Id, b.Balance })
                    .ToListAsync();

                var latestBalances = allBalances
                    .GroupBy(b => b.LoanNo)
                    .Select(g => new
                    {
                        LoanNo = g.Key,
                        Balance = g.OrderByDescending(b => b.Id).FirstOrDefault()?.Balance ?? 0
                    })
                    .ToList();

                foreach (var item in latestBalances)
                {
                    latestBalancesDict[item.LoanNo] = item.Balance;
                }

                // Join in memory to avoid EF Core conversion issues
                var joinedData = loansList
                    .Join(loanTypesList,
                        loan => loan.LoanCode,
                        loanType => loanType.LoanCode,
                        (loan, loanType) => new
                        {
                            loan.LoanNo,
                            loan.LoanCode,
                            loan.CompanyCode,
                            loanType.LoanType1,
                            loanType.LoanProduct,
                            Balance = latestBalancesDict.ContainsKey(loan.LoanNo) ? latestBalancesDict[loan.LoanNo] : 0
                        })
                    .ToList();

                // Group by loan type name
                var records = joinedData
                    .GroupBy(x => string.IsNullOrEmpty(x.LoanType1) ? (x.LoanProduct ?? "Unknown") : x.LoanType1)
                    .Select(g => new PortfolioAtRiskRecord
                    {
                        LoanTypeName = g.Key,
                        OutstandingPrincipal = g.Sum(x => x.Balance),
                        Arrears = 0 // Replace with actual arrears calculation if available
                    })
                    .OrderBy(r => r.LoanTypeName)
                    .ToList();

                // Calculate PAR for each record
                foreach (var record in records)
                {
                    if (record.OutstandingPrincipal > 0)
                        record.PAR = Math.Round((record.Arrears / record.OutstandingPrincipal) * 100, 2);
                    else
                        record.PAR = 0;
                }

                var totalOutstanding = records.Sum(r => r.OutstandingPrincipal);
                var totalArrears = records.Sum(r => r.Arrears);
                var overallPAR = totalOutstanding > 0 ? Math.Round((totalArrears / totalOutstanding) * 100, 2) : 0;

                return new PortfolioAtRiskViewModel
                {
                    Filter = filter,
                    Records = records,
                    TotalOutstandingPrincipal = totalOutstanding,
                    TotalArrears = totalArrears,
                    OverallPAR = overallPAR,
                    TotalRecords = records.Count,
                    GeneratedAt = DateTime.Now
                };
            }
            catch (Exception ex)
            {
                // Log the exception if you have logging
                Console.WriteLine($"Error in PortfolioAtRiskService: {ex.Message}");
                return GetEmptyViewModel(filter);
            }
        }

        private static PortfolioAtRiskViewModel GetEmptyViewModel(PortfolioAtRiskFilter filter)
        {
            return new PortfolioAtRiskViewModel
            {
                Filter = filter,
                Records = new List<PortfolioAtRiskRecord>(),
                TotalOutstandingPrincipal = 0,
                TotalArrears = 0,
                OverallPAR = 0,
                TotalRecords = 0,
                GeneratedAt = DateTime.Now
            };
        }
    }
}