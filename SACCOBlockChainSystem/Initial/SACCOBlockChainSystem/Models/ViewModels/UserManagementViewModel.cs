// Models/ViewModels/UserManagementViewModel.cs
using SACCOBlockChainSystem.Models.DTOs;

namespace SACCOBlockChainSystem.Models.ViewModels
{
    public class UserManagementViewModel
    {
        public List<UserListDTO> Users { get; set; } = new List<UserListDTO>();
        public string SearchTerm { get; set; }
        public bool IsEditMode { get; set; }
        public UserDTO SelectedUser { get; set; }
        public string? UserName { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? UserGroup { get; set; }
        public string? CompanyCode { get; set; }
        public string? Status { get; set; }
        public string? Department { get; set; }
        public int? SubCountyId { get; set; }
        public int? WardId { get; set; }
    }

    public class UserListDTO
    {
        public int UserId { get; set; }
        public string? UserName { get; set; }
        public string? UserLoginId { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Department { get; set; }
        public string? UserGroup { get; set; }
        public string? CompanyCode { get; set; }
        public string? CompanyName { get; set; }
        public string? Status { get; set; }
        public bool IsLocked { get; set; }
    }
}