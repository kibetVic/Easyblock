// Services/InquiryService.cs
using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Models;
using SACCOBlockChainSystem.Models.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SACCOBlockChainSystem.Services
{
    public interface IInquiryService
    {
        Task<MemberInquiryResponseDTO> GetMemberInquiryAsync(string memberNo, string companyCode, string userId);
        Task<ShareInquiryResponseDTO> GetShareInquiryAsync(string memberNo, string companyCode, string userId);
        Task<LoanInquiryResponseDTO> GetLoanInquiryAsync(string memberNo, string companyCode, string userId);
        Task<TransactionInquiryResponseDTO> GetTransactionInquiryAsync(string memberNo, string companyCode, string userId);
        Task<MemberSearchResponseDTO> SearchMembersAsync(MemberSearchDTO searchDto, string companyCode, string userId);
    }

    public class InquiryService : IInquiryService
    {
        private readonly ApplicationDbContext _context;

        public InquiryService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<MemberInquiryResponseDTO> GetMemberInquiryAsync(string memberNo, string companyCode, string userId)
        {
            var member = await _context.Members
                .Include(m => m.NextOfKeens)
                .FirstOrDefaultAsync(m => m.MemberNo == memberNo && m.CompanyCode == companyCode);

            if (member == null)
            {
                throw new Exception($"Member {memberNo} not found");
            }

            // Calculate financial summaries
            var contributions = await _context.Contribs
                .Where(c => c.MemberNo == memberNo && c.CompanyCode == companyCode)
                .SumAsync(c => c.Amount ?? 0);

            var loanBalance = await _context.Loanbal
                .Where(l => l.MemberNo == memberNo && l.Companycode == companyCode && !l.Cleared)
                .SumAsync(l => l.Balance + l.IntrOwed);

            var activeLoans = await _context.Loans
                .Where(l => l.MemberNo == memberNo && l.CompanyCode == companyCode && l.Status != (int?)Status.Closed)
                .CountAsync();

            var totalTransactions = await _context.Transactions
                .Where(t => t.CompanyCode == companyCode && t.TransactionNo != null)
                .Join(_context.Contribs.Where(c => c.MemberNo == memberNo),
                    t => t.TransactionNo,
                    c => c.TransactionNo,
                    (t, c) => t)
                .CountAsync();

            // Calculate share balance by share type
            var shareBalances = await _context.ContribShares
                .Where(cs => cs.MemberNo == memberNo && cs.CompanyCode == companyCode)
                .GroupBy(cs => cs.Sharescode)
                .Select(g => new ShareBalanceByTypeDTO
                {
                    SharesCode = g.Key,
                    SharesType = g.First().SharescodeNavigation != null ? g.First().SharescodeNavigation.SharesType : "",
                    Balance = g.Sum(cs => cs.ShareCapitalAmount ?? 0 + cs.DepositsAmount ?? 0)
                })
                .ToListAsync();

            var response = new MemberInquiryResponseDTO
            {
                MemberNo = member.MemberNo,
                FullName = $"{member.Surname} {member.OtherNames}",
                IdNo = member.Idno,
                PhoneNo = member.PhoneNo,
                Email = member.Email,
                Gender = member.Sex,
                DateOfBirth = member.Dob,
                Age = member.Age,
                Station = member.Station,
                Department = member.Dept,
                Employer = member.Employer,
                MembershipType = member.MembershipType,
                DateJoined = member.ApplicDate,
                Status = member.Status == 1 ? "Active" : "Inactive",
                IsActive = member.Status == 1,
                TotalContributions = contributions,
                TotalLoanBalance = loanBalance,
                ActiveLoansCount = activeLoans,
                TotalTransactions = totalTransactions,
                ShareBalances = shareBalances,
                NextOfKeens = member.NextOfKeens.Select(n => new NextOfKeenSummaryDTO
                {
                    FullName = n.FullName,
                    Relationship = n.Relationship,
                    PhoneNo = n.PhoneNo,
                    IsPrimary = n.IsPrimary,
                    BenefitPercentage = n.BenefitPercentage ?? 0
                }).ToList(),
                InquiryTimestamp = DateTime.Now,
                InquiredBy = userId
            };

            return response;
        }

        public async Task<ShareInquiryResponseDTO> GetShareInquiryAsync(string memberNo, string companyCode, string userId)
        {
            var member = await _context.Members
                .FirstOrDefaultAsync(m => m.MemberNo == memberNo && m.CompanyCode == companyCode);

            if (member == null)
            {
                throw new Exception($"Member {memberNo} not found");
            }

            // Get all share contributions grouped by share type
            var shareContributions = await _context.ContribShares
                .Where(cs => cs.MemberNo == memberNo && cs.CompanyCode == companyCode)
                .Include(cs => cs.SharescodeNavigation)
                .OrderByDescending(cs => cs.ContrDate)
                .ToListAsync();

            // Get share purchases from Contrib table
            var sharePurchases = await _context.Contribs
                .Where(c => c.MemberNo == memberNo && c.CompanyCode == companyCode && c.Sharescode != null)
                .Include(c => c.SharescodeNavigation)
                .OrderByDescending(c => c.ContrDate)
                .ToListAsync();

            // Calculate totals by share type
            var shareTypeSummaries = new Dictionary<string, ShareTypeSummaryDTO>();

            foreach (var cs in shareContributions)
            {
                var code = cs.Sharescode ?? "Unknown";
                if (!shareTypeSummaries.ContainsKey(code))
                {
                    shareTypeSummaries[code] = new ShareTypeSummaryDTO
                    {
                        SharesCode = code,
                        SharesType = cs.SharescodeNavigation?.SharesType ?? "Unknown",
                        TotalShares = 0,
                        ShareCapital = 0,
                        Deposits = 0,
                        RegFees = 0,
                        Donations = 0,
                        LoanAllocations = 0,
                        PassBook = 0,
                        Transactions = new List<ShareTransactionDetailDTO>()
                    };
                }

                shareTypeSummaries[code].ShareCapital += cs.ShareCapitalAmount ?? 0;
                shareTypeSummaries[code].Deposits += cs.DepositsAmount ?? 0;
                shareTypeSummaries[code].RegFees += cs.RegFeeAmount ?? 0;
                shareTypeSummaries[code].Donations += cs.Donor ?? 0;
                shareTypeSummaries[code].LoanAllocations += cs.LoanAmount ?? 0;
                shareTypeSummaries[code].PassBook += cs.PassBookAmount ?? 0;
                shareTypeSummaries[code].TotalShares = shareTypeSummaries[code].ShareCapital + shareTypeSummaries[code].Deposits;

                shareTypeSummaries[code].Transactions.Add(new ShareTransactionDetailDTO
                {
                    TransactionDate = cs.ContrDate ?? DateTime.Now,
                    TransactionType = "Contribution",
                    Amount = (cs.ShareCapitalAmount ?? 0) + (cs.DepositsAmount ?? 0),
                    ShareCapital = cs.ShareCapitalAmount ?? 0,
                    Deposits = cs.DepositsAmount ?? 0,
                    ReceiptNo = cs.ReceiptNo,
                    Remarks = cs.Remarks,
                    BlockchainTxId = cs.BlockchainTxId
                });
            }

            foreach (var cp in sharePurchases)
            {
                var code = cp.Sharescode ?? "Unknown";
                if (!shareTypeSummaries.ContainsKey(code))
                {
                    shareTypeSummaries[code] = new ShareTypeSummaryDTO
                    {
                        SharesCode = code,
                        SharesType = cp.SharescodeNavigation?.SharesType ?? "Unknown",
                        TotalShares = 0,
                        ShareCapital = 0,
                        Deposits = 0,
                        RegFees = 0,
                        Donations = 0,
                        LoanAllocations = 0,
                        PassBook = 0,
                        Transactions = new List<ShareTransactionDetailDTO>()
                    };
                }

                shareTypeSummaries[code].ShareCapital += cp.Amount ?? 0;
                shareTypeSummaries[code].TotalShares = shareTypeSummaries[code].ShareCapital + shareTypeSummaries[code].Deposits;

                shareTypeSummaries[code].Transactions.Add(new ShareTransactionDetailDTO
                {
                    TransactionDate = cp.ContrDate ?? DateTime.Now,
                    TransactionType = "Purchase",
                    Amount = cp.Amount ?? 0,
                    ShareCapital = cp.Amount ?? 0,
                    Deposits = 0,
                    ReceiptNo = cp.ReceiptNo,
                    Remarks = cp.Remarks,
                    BlockchainTxId = cp.BlockchainTxId
                });
            }

            // Calculate shares locked for guarantees
            var lockedShares = await _context.Loanguar
                .Where(lg => lg.MemberNo == memberNo && lg.CompanyCode == companyCode)
                .SumAsync(lg => lg.Balance ?? 0);

            var response = new ShareInquiryResponseDTO
            {
                MemberNo = member.MemberNo,
                MemberName = $"{member.Surname} {member.OtherNames}",
                TotalShareBalance = shareTypeSummaries.Values.Sum(s => s.TotalShares),
                TotalShareCapital = shareTypeSummaries.Values.Sum(s => s.ShareCapital),
                TotalDeposits = shareTypeSummaries.Values.Sum(s => s.Deposits),
                LockedForGuarantees = lockedShares,
                AvailableShares = shareTypeSummaries.Values.Sum(s => s.TotalShares) - lockedShares,
                ShareTypeSummaries = shareTypeSummaries.Values.ToList(),
                InquiryTimestamp = DateTime.Now,
                InquiredBy = userId
            };

            return response;
        }

        public async Task<LoanInquiryResponseDTO> GetLoanInquiryAsync(string memberNo, string companyCode, string userId)
        {
            var member = await _context.Members
                .FirstOrDefaultAsync(m => m.MemberNo == memberNo && m.CompanyCode == companyCode);

            if (member == null)
            {
                throw new Exception($"Member {memberNo} not found");
            }

            // Get all loans for member
            var loans = await _context.Loans
                .Where(l => l.MemberNo == memberNo && l.CompanyCode == companyCode)
                .OrderByDescending(l => l.ApplicDate)
                .ToListAsync();

            var loanDetails = new List<LoanDetailDTO>();

            foreach (var loan in loans)
            {
                var loanBal = await _context.Loanbal
                    .FirstOrDefaultAsync(lb => lb.LoanNo == loan.LoanNo && lb.Companycode == companyCode);

                var guarantors = await _context.Loanguar
                    .Where(lg => lg.LoanNo == loan.LoanNo && lg.CompanyCode == companyCode)
                    .ToListAsync();

                var repayments = await _context.Repay
                    .Where(r => r.LoanNo == loan.LoanNo && r.CompanyCode == companyCode)
                    .OrderByDescending(r => r.DateReceived)
                    .Take(10)
                    .ToListAsync();

                // Get loan type details
                var loanType = await _context.Loantypes
                    .FirstOrDefaultAsync(lt => lt.LoanCode == loan.LoanCode && lt.CompanyCode == companyCode);

                loanDetails.Add(new LoanDetailDTO
                {
                    LoanNo = loan.LoanNo,
                    LoanCode = loan.LoanCode,
                    LoanType = loanType?.LoanType1 ?? "Unknown",
                    PrincipalAmount = loan.LoanAmt ?? 0,
                    ApprovedAmount = loan.Aamount ?? 0,
                    ApplicationDate = loan.ApplicDate,
                    DisbursementDate = loan.TransactionNo != null ? loan.AuditDateTime : null,
                    InterestRate = loan.Interest ?? 0,
                    RepaymentPeriod = loan.RepayPeriod ?? 0,
                    RepaymentMethod = loan.RepayMethod ?? "Monthly",
                    Purpose = loan.Purpose,
                    Status = GetLoanStatusString(loan.Status),
                    OutstandingBalance = loanBal?.Balance ?? 0,
                    OutstandingInterest = (loanBal?.IntrOwed ?? 0) + (loanBal?.Penalty ?? 0),
                    TotalRepaid = repayments.Sum(r => r.Amount ?? 0),
                    LastPaymentDate = repayments.FirstOrDefault()?.DateReceived,
                    NextDueDate = loanBal?.Nextduedate,
                    IsOverdue = loanBal?.Nextduedate < DateTime.Now && (loanBal?.Balance ?? 0) > 0,
                    Guarantors = guarantors.Select(g => new GuarantorInfoDTO
                    {
                        MemberNo = g.MemberNo,
                        FullName = g.FullNames,
                        GuaranteeAmount = g.Amount ?? 0,
                        Balance = g.Balance ?? 0
                    }).ToList(),
                    RecentRepayments = repayments.Select(r => new RepaymentInfoDTO
                    {
                        PaymentDate = r.DateReceived ?? DateTime.Now,
                        Amount = r.Amount ?? 0,
                        Principal = r.Principal ?? 0,
                        Interest = r.Interest ?? 0,
                        Penalty = r.Penalty ?? 0,
                        ReceiptNo = r.ReceiptNo,
                        BalanceAfter = r.LoanBalance ?? 0
                    }).ToList()
                });
            }

            // Calculate loan statistics
            var activeLoans = loanDetails.Where(l => l.Status == "Active" || l.Status == "Disbursed").ToList();
            var overdueLoans = loanDetails.Where(l => l.IsOverdue).ToList();
            var completedLoans = loanDetails.Where(l => l.Status == "Closed").ToList();

            var response = new LoanInquiryResponseDTO
            {
                MemberNo = member.MemberNo,
                MemberName = $"{member.Surname} {member.OtherNames}",
                TotalLoans = loanDetails.Count,
                TotalBorrowed = loanDetails.Sum(l => l.DisbursedAmount > 0 ? l.DisbursedAmount : l.PrincipalAmount),
                TotalOutstanding = loanDetails.Sum(l => l.OutstandingBalance + l.OutstandingInterest),
                TotalRepaid = loanDetails.Sum(l => l.TotalRepaid),
                ActiveLoansCount = activeLoans.Count,
                OverdueLoansCount = overdueLoans.Count,
                CompletedLoansCount = completedLoans.Count,
                Loans = loanDetails,
                InquiryTimestamp = DateTime.Now,
                InquiredBy = userId
            };

            return response;
        }

        public async Task<TransactionInquiryResponseDTO> GetTransactionInquiryAsync(string memberNo, string companyCode, string userId)
        {
            var member = await _context.Members
                .FirstOrDefaultAsync(m => m.MemberNo == memberNo && m.CompanyCode == companyCode);

            if (member == null)
            {
                throw new Exception($"Member {memberNo} not found");
            }

            // Get all contributions
            var contributions = await _context.Contribs
                .Where(c => c.MemberNo == memberNo && c.CompanyCode == companyCode)
                .Include(c => c.SharescodeNavigation)
                .OrderByDescending(c => c.ContrDate)
                .Take(100)
                .ToListAsync();

            // Get all share contributions
            var shareContributions = await _context.ContribShares
                .Where(cs => cs.MemberNo == memberNo && cs.CompanyCode == companyCode)
                .Include(cs => cs.SharescodeNavigation)
                .OrderByDescending(cs => cs.ContrDate)
                .Take(100)
                .ToListAsync();

            // Get all loan repayments
            var repayments = await _context.Repay
                .Where(r => r.MemberNo == memberNo && r.CompanyCode == companyCode)
                .OrderByDescending(r => r.DateReceived)
                .Take(100)
                .ToListAsync();

            // Combine transactions
            var transactions = new List<TransactionDetailDTO>();

            foreach (var c in contributions)
            {
                transactions.Add(new TransactionDetailDTO
                {
                    TransactionDate = c.ContrDate ?? DateTime.Now,
                    TransactionType = "Share Purchase",
                    Description = $"Purchase of {c.SharescodeNavigation?.SharesType ?? "Shares"}",
                    Debit = c.Amount ?? 0,
                    Credit = 0,
                    Balance = c.ShareBal ?? 0,
                    Reference = c.ReceiptNo,
                    BlockchainTxId = c.BlockchainTxId,
                    ProcessedBy = c.TransBy
                });
            }

            foreach (var cs in shareContributions)
            {
                var amount = (cs.ShareCapitalAmount ?? 0) + (cs.DepositsAmount ?? 0);
                if (amount > 0)
                {
                    transactions.Add(new TransactionDetailDTO
                    {
                        TransactionDate = cs.ContrDate ?? DateTime.Now,
                        TransactionType = "Share Contribution",
                        Description = $"{cs.SharescodeNavigation?.SharesType ?? "Shares"} Contribution",
                        Debit = amount,
                        Credit = 0,
                        Balance = 0,
                        Reference = cs.ReceiptNo,
                        BlockchainTxId = cs.BlockchainTxId,
                        ProcessedBy = null
                    });
                }
            }

            foreach (var r in repayments)
            {
                transactions.Add(new TransactionDetailDTO
                {
                    TransactionDate = r.DateReceived ?? DateTime.Now,
                    TransactionType = "Loan Repayment",
                    Description = $"Repayment for Loan {r.LoanNo}",
                    Debit = 0,
                    Credit = r.Amount ?? 0,
                    Balance = r.LoanBalance ?? 0,
                    Reference = r.ReceiptNo,
                    BlockchainTxId = r.BlockchainTxId,
                    ProcessedBy = r.Transby
                });
            }

            transactions = transactions.OrderByDescending(t => t.TransactionDate).ToList();

            // Calculate totals
            var totalDeposits = transactions.Where(t => t.TransactionType == "Share Purchase" || t.TransactionType == "Share Contribution").Sum(t => t.Debit);
            var totalWithdrawals = transactions.Where(t => t.TransactionType == "Loan Repayment").Sum(t => t.Credit);

            var response = new TransactionInquiryResponseDTO
            {
                MemberNo = member.MemberNo,
                MemberName = $"{member.Surname} {member.OtherNames}",
                TotalTransactions = transactions.Count,
                TotalDeposits = totalDeposits,
                TotalWithdrawals = totalWithdrawals,
                NetPosition = totalDeposits - totalWithdrawals,
                Transactions = transactions,
                InquiryTimestamp = DateTime.Now,
                InquiredBy = userId
            };

            return response;
        }

        public async Task<MemberSearchResponseDTO> SearchMembersAsync(MemberSearchDTO searchDto, string companyCode, string userId)
        {
            var query = _context.Members
                .Where(m => m.CompanyCode == companyCode);

            // Apply search filters
            if (!string.IsNullOrEmpty(searchDto.MemberNo))
            {
                query = query.Where(m => m.MemberNo.Contains(searchDto.MemberNo));
            }

            if (!string.IsNullOrEmpty(searchDto.FullName))
            {
                query = query.Where(m => (m.Surname + " " + m.OtherNames).Contains(searchDto.FullName));
            }

            if (!string.IsNullOrEmpty(searchDto.IdNo))
            {
                query = query.Where(m => m.Idno == searchDto.IdNo);
            }

            if (!string.IsNullOrEmpty(searchDto.PhoneNo))
            {
                query = query.Where(m => m.PhoneNo == searchDto.PhoneNo || m.MobileNo == searchDto.PhoneNo);
            }

            if (!string.IsNullOrEmpty(searchDto.Email))
            {
                query = query.Where(m => m.Email == searchDto.Email || m.EmailAddress == searchDto.Email);
            }

            if (!string.IsNullOrEmpty(searchDto.Department))
            {
                query = query.Where(m => m.Dept == searchDto.Department);
            }

            if (!string.IsNullOrEmpty(searchDto.Station))
            {
                query = query.Where(m => m.Station == searchDto.Station);
            }

            if (searchDto.Status.HasValue)
            {
                query = query.Where(m => m.Status == searchDto.Status.Value);
            }

            if (searchDto.FromDate.HasValue)
            {
                query = query.Where(m => m.ApplicDate >= searchDto.FromDate.Value);
            }

            if (searchDto.ToDate.HasValue)
            {
                query = query.Where(m => m.ApplicDate <= searchDto.ToDate.Value);
            }

            var totalCount = await query.CountAsync();

            var members = await query
                .OrderBy(m => m.MemberNo)
                .Skip((searchDto.Page - 1) * searchDto.PageSize)
                .Take(searchDto.PageSize)
                .Select(m => new MemberSearchResultDTO
                {
                    MemberNo = m.MemberNo,
                    FullName = m.Surname + " " + m.OtherNames,
                    IdNo = m.Idno,
                    PhoneNo = m.PhoneNo ?? m.MobileNo,
                    Email = m.Email ?? m.EmailAddress,
                    Department = m.Dept,
                    Station = m.Station,
                    Status = m.Status == 1 ? "Active" : "Inactive",
                    DateJoined = m.ApplicDate ?? DateTime.Now,
                    ShareBalance = m.ShareCap ?? 0
                })
                .ToListAsync();

            var response = new MemberSearchResponseDTO
            {
                SearchCriteria = searchDto,
                TotalCount = totalCount,
                Page = searchDto.Page,
                PageSize = searchDto.PageSize,
                TotalPages = (int)Math.Ceiling((double)totalCount / searchDto.PageSize),
                Members = members,
                InquiryTimestamp = DateTime.Now,
                InquiredBy = userId
            };

            return response;
        }

        private string GetLoanStatusString(int? status)
        {
            return status switch
            {
                1 => "Draft",
                2 => "Submitted",
                3 => "Under Appraisal",
                4 => "Approved",
                5 => "Endorsed",
                6 => "Disbursed",
                7 => "Closed",
                8 => "Defaulted",
                9 => "Written Off",
                10 => "Rejected",
                _ => "Unknown"
            };
        }
    }
}