using Microsoft.EntityFrameworkCore;
using SACCOBlockChainSystem.Data;
using SACCOBlockChainSystem.Models;
using SACCOBlockChainSystem.Models.DTOs;

namespace SACCOBlockChainSystem.Services
{
    public interface IShareTransferService
    {
        Task<ShareTransfer> GetTransferByIdAsync(int id);
        Task<List<ShareTransferResponseDTO>> GetPendingTransfersAsync(string companyCode);
        Task<List<ShareTransferResponseDTO>> GetTransfersByStatusAsync(string status, string companyCode);
        Task<List<ShareTransferResponseDTO>> GetTransfersByTransferorAsync(string memberNo, string companyCode);
        Task<List<ShareTransferResponseDTO>> GetTransfersByTransfereeAsync(string memberNo, string companyCode);
    }

    public class ShareTransferService : IShareTransferService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<ShareTransferService> _logger;

        public ShareTransferService(
            ApplicationDbContext context,
            ILogger<ShareTransferService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<ShareTransfer> GetTransferByIdAsync(int id)
        {
            return await _context.ShareTransfers
                .Include(t => t.Transferor)
                .Include(t => t.Transferee)
                .Include(t => t.ShareType)
                .Include(t => t.Approvals)
                .FirstOrDefaultAsync(t => t.Id == id);
        }

        public async Task<List<ShareTransferResponseDTO>> GetPendingTransfersAsync(string companyCode)
        {
            return await _context.ShareTransfers
                .Where(t => t.CompanyCode == companyCode && t.Status == "Pending")
                .OrderBy(t => t.CreatedAt)
                .Select(t => new ShareTransferResponseDTO
                {
                    Id = t.Id,
                    TransferNo = t.TransferNo,
                    TransferorMemberNo = t.TransferorMemberNo,
                    TransferorName = t.Transferor != null ? $"{t.Transferor.Surname} {t.Transferor.OtherNames}" : "",
                    TransfereeMemberNo = t.TransfereeMemberNo,
                    TransfereeName = t.Transferee != null ? $"{t.Transferee.Surname} {t.Transferee.OtherNames}" : "",
                    SharesCode = t.SharesCode,
                    SharesType = t.ShareType != null ? t.ShareType.SharesType ?? t.SharesCode : t.SharesCode,
                    NumberOfShares = t.NumberOfShares,
                    PricePerShare = t.PricePerShare,
                    TotalAmount = t.TotalAmount,
                    TransferDate = t.TransferDate,
                    TransferType = t.TransferType,
                    Status = t.Status,
                    TransferFee = t.TransferFee,
                    StampDuty = t.StampDuty,
                    TotalCharges = t.TotalCharges,
                    ApprovedBy = t.ApprovedBy,
                    ApprovalDate = t.ApprovalDate,
                    BlockchainTxId = t.BlockchainTxId,
                    CreatedAt = t.CreatedAt
                })
                .ToListAsync();
        }

        public async Task<List<ShareTransferResponseDTO>> GetTransfersByStatusAsync(string status, string companyCode)
        {
            return await _context.ShareTransfers
                .Where(t => t.CompanyCode == companyCode && t.Status == status)
                .OrderByDescending(t => t.CreatedAt)
                .Select(t => new ShareTransferResponseDTO
                {
                    Id = t.Id,
                    TransferNo = t.TransferNo,
                    TransferorMemberNo = t.TransferorMemberNo,
                    TransferorName = t.Transferor != null ? $"{t.Transferor.Surname} {t.Transferor.OtherNames}" : "",
                    TransfereeMemberNo = t.TransfereeMemberNo,
                    TransfereeName = t.Transferee != null ? $"{t.Transferee.Surname} {t.Transferee.OtherNames}" : "",
                    SharesCode = t.SharesCode,
                    SharesType = t.ShareType != null ? t.ShareType.SharesType ?? t.SharesCode : t.SharesCode,
                    NumberOfShares = t.NumberOfShares,
                    PricePerShare = t.PricePerShare,
                    TotalAmount = t.TotalAmount,
                    TransferDate = t.TransferDate,
                    TransferType = t.TransferType,
                    Status = t.Status,
                    TransferFee = t.TransferFee,
                    StampDuty = t.StampDuty,
                    TotalCharges = t.TotalCharges,
                    ApprovedBy = t.ApprovedBy,
                    ApprovalDate = t.ApprovalDate,
                    BlockchainTxId = t.BlockchainTxId,
                    CreatedAt = t.CreatedAt
                })
                .ToListAsync();
        }

        public async Task<List<ShareTransferResponseDTO>> GetTransfersByTransferorAsync(string memberNo, string companyCode)
        {
            return await _context.ShareTransfers
                .Where(t => t.TransferorMemberNo == memberNo && t.CompanyCode == companyCode)
                .OrderByDescending(t => t.CreatedAt)
                .Select(t => new ShareTransferResponseDTO
                {
                    Id = t.Id,
                    TransferNo = t.TransferNo,
                    TransferorMemberNo = t.TransferorMemberNo,
                    TransferorName = t.Transferor != null ? $"{t.Transferor.Surname} {t.Transferor.OtherNames}" : "",
                    TransfereeMemberNo = t.TransfereeMemberNo,
                    TransfereeName = t.Transferee != null ? $"{t.Transferee.Surname} {t.Transferee.OtherNames}" : "",
                    SharesCode = t.SharesCode,
                    SharesType = t.ShareType != null ? t.ShareType.SharesType ?? t.SharesCode : t.SharesCode,
                    NumberOfShares = t.NumberOfShares,
                    PricePerShare = t.PricePerShare,
                    TotalAmount = t.TotalAmount,
                    TransferDate = t.TransferDate,
                    TransferType = t.TransferType,
                    Status = t.Status,
                    TransferFee = t.TransferFee,
                    StampDuty = t.StampDuty,
                    TotalCharges = t.TotalCharges,
                    ApprovedBy = t.ApprovedBy,
                    ApprovalDate = t.ApprovalDate,
                    BlockchainTxId = t.BlockchainTxId,
                    CreatedAt = t.CreatedAt
                })
                .ToListAsync();
        }

        public async Task<List<ShareTransferResponseDTO>> GetTransfersByTransfereeAsync(string memberNo, string companyCode)
        {
            return await _context.ShareTransfers
                .Where(t => t.TransfereeMemberNo == memberNo && t.CompanyCode == companyCode)
                .OrderByDescending(t => t.CreatedAt)
                .Select(t => new ShareTransferResponseDTO
                {
                    Id = t.Id,
                    TransferNo = t.TransferNo,
                    TransferorMemberNo = t.TransferorMemberNo,
                    TransferorName = t.Transferor != null ? $"{t.Transferor.Surname} {t.Transferor.OtherNames}" : "",
                    TransfereeMemberNo = t.TransfereeMemberNo,
                    TransfereeName = t.Transferee != null ? $"{t.Transferee.Surname} {t.Transferee.OtherNames}" : "",
                    SharesCode = t.SharesCode,
                    SharesType = t.ShareType != null ? t.ShareType.SharesType ?? t.SharesCode : t.SharesCode,
                    NumberOfShares = t.NumberOfShares,
                    PricePerShare = t.PricePerShare,
                    TotalAmount = t.TotalAmount,
                    TransferDate = t.TransferDate,
                    TransferType = t.TransferType,
                    Status = t.Status,
                    TransferFee = t.TransferFee,
                    StampDuty = t.StampDuty,
                    TotalCharges = t.TotalCharges,
                    ApprovedBy = t.ApprovedBy,
                    ApprovalDate = t.ApprovalDate,
                    BlockchainTxId = t.BlockchainTxId,
                    CreatedAt = t.CreatedAt
                })
                .ToListAsync();
        }
    }
}