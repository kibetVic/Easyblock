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
    public interface IUserService
    {
        // User retrieval methods
        Task<List<UserListDTO>> GetAllUsersAsync(string searchTerm);
        Task<List<UserListDTO>> GetUsersByCompanyAsync(string companyCode, string searchTerm);
        Task<List<UserListDTO>> GetUsersByCompanyAndRoleAsync(string companyCode, string role, string searchTerm);
        Task<UserAccounts1> GetUserByIdAsync(int userId);
        Task<UserAccounts1> GetUserByUsernameAsync(string username);

        // User management methods
        Task<bool> CreateUserAsync(UserAccounts1 user);
        Task<bool> UpdateUserAsync(UserAccounts1 user);
        Task<bool> DeleteUserAsync(int userId);
        Task<bool> LockUserAsync(int userId);
        Task<bool> UnlockUserAsync(int userId);
        Task<bool> ApproveUserAsync(int userId);
        Task<bool> ResetUserPasswordAsync(int userId, string newPassword);

        // Helper methods
        Task<List<string>> GetUserGroupsAsync();
        Task<List<object>> GetCompaniesForDropdownAsync();
        Task<List<object>> GetSubCountiesForDropdownAsync();
        Task<List<object>> GetWardsBySubCountyAsync(int subCountyId);
        Task<int> GetFailedAttemptsAsync(int userId);
        Task<bool> IsUserLockedAsync(int userId);

        // Statistics
        Task<int> GetTotalUsersCountAsync(string companyCode = null);
        Task<int> GetActiveUsersCountAsync(string companyCode = null);
        Task<int> GetLockedUsersCountAsync(string companyCode = null);
        Task<int> GetPendingUsersCountAsync(string companyCode = null);
    }

    public class UserService : IUserService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<UserService> _logger;

        public UserService(ApplicationDbContext context, ILogger<UserService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<List<UserListDTO>> GetAllUsersAsync(string searchTerm)
        {
            try
            {
                var query = _context.UserAccounts1.AsQueryable();

                if (!string.IsNullOrEmpty(searchTerm))
                {
                    query = query.Where(u => u.UserName.Contains(searchTerm) ||
                                              u.Email.Contains(searchTerm) ||
                                              u.UserLoginId.Contains(searchTerm) ||
                                              (u.Phone != null && u.Phone.Contains(searchTerm)));
                }

                return await query
                    .OrderByDescending(u => u.DateCreated)
                    .Select(u => new UserListDTO
                    {
                        UserId = u.UserId,
                        UserName = u.UserName,
                        UserLoginId = u.UserLoginId,
                        UserGroup = u.UserGroup,
                        CompanyCode = u.CompanyCode,
                        Email = u.Email,
                        Phone = u.Phone,
                        Status = u.Status,
                        DateCreated = u.DateCreated,
                        IsLocked = u.IsLocked ?? false
                    })
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetAllUsersAsync");
                return new List<UserListDTO>();
            }
        }

        public async Task<List<UserListDTO>> GetUsersByCompanyAsync(string companyCode, string searchTerm)
        {
            try
            {
                var query = _context.UserAccounts1
                    .Where(u => u.CompanyCode == companyCode)
                    .AsQueryable();

                if (!string.IsNullOrEmpty(searchTerm))
                {
                    query = query.Where(u => u.UserName.Contains(searchTerm) ||
                                              u.Email.Contains(searchTerm) ||
                                              u.UserLoginId.Contains(searchTerm) ||
                                              (u.Phone != null && u.Phone.Contains(searchTerm)));
                }

                return await query
                    .OrderByDescending(u => u.DateCreated)
                    .Select(u => new UserListDTO
                    {
                        UserId = u.UserId,
                        UserName = u.UserName,
                        UserLoginId = u.UserLoginId,
                        UserGroup = u.UserGroup,
                        CompanyCode = u.CompanyCode,
                        Email = u.Email,
                        Phone = u.Phone,
                        Status = u.Status,
                        DateCreated = u.DateCreated,
                        IsLocked = u.IsLocked ?? false
                    })
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in GetUsersByCompanyAsync for company {companyCode}");
                return new List<UserListDTO>();
            }
        }

        public async Task<List<UserListDTO>> GetUsersByCompanyAndRoleAsync(string companyCode, string role, string searchTerm)
        {
            try
            {
                var query = _context.UserAccounts1
                    .Where(u => u.CompanyCode == companyCode && u.UserGroup == role)
                    .AsQueryable();

                if (!string.IsNullOrEmpty(searchTerm))
                {
                    query = query.Where(u => u.UserName.Contains(searchTerm) ||
                                              u.Email.Contains(searchTerm) ||
                                              u.UserLoginId.Contains(searchTerm) ||
                                              (u.Phone != null && u.Phone.Contains(searchTerm)));
                }

                return await query
                    .OrderByDescending(u => u.DateCreated)
                    .Select(u => new UserListDTO
                    {
                        UserId = u.UserId,
                        UserName = u.UserName,
                        UserLoginId = u.UserLoginId,
                        UserGroup = u.UserGroup,
                        CompanyCode = u.CompanyCode,
                        Email = u.Email,
                        Phone = u.Phone,
                        Status = u.Status,
                        DateCreated = u.DateCreated,
                        IsLocked = u.IsLocked ?? false
                    })
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in GetUsersByCompanyAndRoleAsync for company {companyCode}, role {role}");
                return new List<UserListDTO>();
            }
        }

        public async Task<UserAccounts1> GetUserByIdAsync(int userId)
        {
            try
            {
                return await _context.UserAccounts1
                    .FirstOrDefaultAsync(u => u.UserId == userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in GetUserByIdAsync for userId {userId}");
                return null;
            }
        }

        public async Task<UserAccounts1> GetUserByUsernameAsync(string username)
        {
            try
            {
                return await _context.UserAccounts1
                    .FirstOrDefaultAsync(u => u.UserName == username);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in GetUserByUsernameAsync for username {username}");
                return null;
            }
        }

        public async Task<bool> CreateUserAsync(UserAccounts1 user)
        {
            try
            {
                await _context.UserAccounts1.AddAsync(user);
                await _context.SaveChangesAsync();
                _logger.LogInformation($"User {user.UserName} created successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in CreateUserAsync for user {user.UserName}");
                return false;
            }
        }

        public async Task<bool> UpdateUserAsync(UserAccounts1 user)
        {
            try
            {
                _context.UserAccounts1.Update(user);
                await _context.SaveChangesAsync();
                _logger.LogInformation($"User {user.UserName} updated successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in UpdateUserAsync for user {user.UserName}");
                return false;
            }
        }

        public async Task<bool> DeleteUserAsync(int userId)
        {
            try
            {
                var user = await GetUserByIdAsync(userId);
                if (user != null)
                {
                    _context.UserAccounts1.Remove(user);
                    await _context.SaveChangesAsync();
                    _logger.LogInformation($"User {user.UserName} deleted successfully");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in DeleteUserAsync for userId {userId}");
                return false;
            }
        }

        public async Task<bool> LockUserAsync(int userId)
        {
            try
            {
                var user = await GetUserByIdAsync(userId);
                if (user != null)
                {
                    user.IsLocked = true;
                    user.Status = "Locked";
                    user.Userstatus = "Locked";
                    await _context.SaveChangesAsync();
                    _logger.LogInformation($"User {user.UserName} locked successfully");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in LockUserAsync for userId {userId}");
                return false;
            }
        }

        public async Task<bool> UnlockUserAsync(int userId)
        {
            try
            {
                var user = await GetUserByIdAsync(userId);
                if (user != null)
                {
                    user.IsLocked = false;
                    user.FailedAttempts = 0;
                    if (user.Status == "Locked")
                    {
                        user.Status = "Active";
                        user.Userstatus = "Active";
                    }
                    await _context.SaveChangesAsync();
                    _logger.LogInformation($"User {user.UserName} unlocked successfully");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in UnlockUserAsync for userId {userId}");
                return false;
            }
        }

        public async Task<bool> ApproveUserAsync(int userId)
        {
            try
            {
                var user = await GetUserByIdAsync(userId);
                if (user != null)
                {
                    user.Status = "Active";
                    user.Userstatus = "Active";
                    user.ApprovalStatus = "Approved";
                    await _context.SaveChangesAsync();
                    _logger.LogInformation($"User {user.UserName} approved successfully");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in ApproveUserAsync for userId {userId}");
                return false;
            }
        }

        public async Task<bool> ResetUserPasswordAsync(int userId, string newPassword)
        {
            try
            {
                var user = await GetUserByIdAsync(userId);
                if (user != null)
                {
                    // Hash the password (assuming you have a HashPassword method)
                    user.Password = HashPassword(newPassword);
                    user.PasswordStatus = "Reset";
                    await _context.SaveChangesAsync();
                    _logger.LogInformation($"Password reset for user {user.UserName}");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in ResetUserPasswordAsync for userId {userId}");
                return false;
            }
        }

        public async Task<List<string>> GetUserGroupsAsync()
        {
            try
            {
                return await _context.UserAccounts1
                    .Where(u => u.UserGroup != null)
                    .Select(u => u.UserGroup)
                    .Distinct()
                    .OrderBy(g => g)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetUserGroupsAsync");
                return new List<string>
                {
                    "Member", "Teller", "LoanOfficer", "Auditor",
                    "Super Admin", "System Administrator", "Book Keeper",
                    "Finance Officer", "BoardMember", "Staff"
                };
            }
        }

        public async Task<List<object>> GetCompaniesForDropdownAsync()
        {
            try
            {
                return await _context.Companies
                    .Where(c => c.Project == true)
                    .OrderBy(c => c.CompanyName)
                    .Select(c => new
                    {
                        c.CompanyCode,
                        DisplayText = $"{c.CompanyCode} - {c.CompanyName}"
                    })
                    .ToListAsync<object>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetCompaniesForDropdownAsync");
                return new List<object>();
            }
        }

        public async Task<List<object>> GetSubCountiesForDropdownAsync()
        {
            try
            {
                return await _context.SubCounties
                    .Where(s => s.Status == "Active")
                    .OrderBy(s => s.SubCountyName)
                    .Select(s => new
                    {
                        s.Id,
                        s.SubCountyName,
                        s.SubCountyCode
                    })
                    .ToListAsync<object>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetSubCountiesForDropdownAsync");
                return new List<object>();
            }
        }

        public async Task<List<object>> GetWardsBySubCountyAsync(int subCountyId)
        {
            try
            {
                return await _context.Wards
                    .Where(w => w.SubCountyId == subCountyId && w.Status == "Active")
                    .OrderBy(w => w.WardName)
                    .Select(w => new
                    {
                        w.Id,
                        w.WardName,
                        w.WardCode
                    })
                    .ToListAsync<object>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in GetWardsBySubCountyAsync for subCountyId {subCountyId}");
                return new List<object>();
            }
        }

        public async Task<int> GetFailedAttemptsAsync(int userId)
        {
            try
            {
                var user = await GetUserByIdAsync(userId);
                return user?.FailedAttempts ?? 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in GetFailedAttemptsAsync for userId {userId}");
                return 0;
            }
        }

        public async Task<bool> IsUserLockedAsync(int userId)
        {
            try
            {
                var user = await GetUserByIdAsync(userId);
                return user?.IsLocked ?? false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in IsUserLockedAsync for userId {userId}");
                return false;
            }
        }

        public async Task<int> GetTotalUsersCountAsync(string companyCode = null)
        {
            try
            {
                var query = _context.UserAccounts1.AsQueryable();
                if (!string.IsNullOrEmpty(companyCode))
                {
                    query = query.Where(u => u.CompanyCode == companyCode);
                }
                return await query.CountAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetTotalUsersCountAsync");
                return 0;
            }
        }

        public async Task<int> GetActiveUsersCountAsync(string companyCode = null)
        {
            try
            {
                var query = _context.UserAccounts1
                    .Where(u => u.Status == "Active" && u.Userstatus == "Active" && u.IsLocked == false);

                if (!string.IsNullOrEmpty(companyCode))
                {
                    query = query.Where(u => u.CompanyCode == companyCode);
                }
                return await query.CountAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetActiveUsersCountAsync");
                return 0;
            }
        }

        public async Task<int> GetLockedUsersCountAsync(string companyCode = null)
        {
            try
            {
                var query = _context.UserAccounts1.Where(u => u.IsLocked == true);

                if (!string.IsNullOrEmpty(companyCode))
                {
                    query = query.Where(u => u.CompanyCode == companyCode);
                }
                return await query.CountAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetLockedUsersCountAsync");
                return 0;
            }
        }

        public async Task<int> GetPendingUsersCountAsync(string companyCode = null)
        {
            try
            {
                var query = _context.UserAccounts1
                    .Where(u => u.Status == "Pending" && u.ApprovalStatus == "Pending");

                if (!string.IsNullOrEmpty(companyCode))
                {
                    query = query.Where(u => u.CompanyCode == companyCode);
                }
                return await query.CountAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetPendingUsersCountAsync");
                return 0;
            }
        }

        // Helper method to hash passwords
        private string HashPassword(string password)
        {
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                var hashedBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password));
                return Convert.ToBase64String(hashedBytes);
            }
        }
    }
}