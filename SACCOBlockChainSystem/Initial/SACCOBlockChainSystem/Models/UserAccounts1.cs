using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SACCOBlockChainSystem.Models;

public partial class UserAccounts1
{
    [Key]
    public int UserId { get; set; }
    //public int UserGroupId { get; set; }
    public string? UserName { get; set; }

    public string UserLoginId { get; set; } = null!;

    public string? Password { get; set; }

    public string? UserGroup { get; set; }

    public string? Cigcode { get; set; }

    public string? CompanyCode { get; set; }

    public string? PassExpire { get; set; }

    public DateTime? DateCreated { get; set; }

    public long? Superuser { get; set; }

    public string? MemberNo { get; set; }

    public string? AssignGl { get; set; }

    public string? DepCode { get; set; }

    public string? Levels { get; set; }

    public bool? Authorize { get; set; }

    public string? Status { get; set; }

    public string? Department { get; set; }

    public string? SubCounty { get; set; }

    public string? Ward { get; set; }

    public string? Sign { get; set; }

    public string? Expirydate { get; set; }

    public string? Userstatus { get; set; }

    public string? PasswordStatus { get; set; }

    public string? Euser { get; set; }

    public long? VendorId { get; set; }

    public long? Count { get; set; }

    public string? Email { get; set; }

    public string? Branchcode { get; set; }

    public string? ApprovalStatus { get; set; }

    public int? FailedAttempts { get; set; }

    public bool? IsLocked { get; set; }

    public string? Phone { get; set; }

    public string? PhoneNo { get; set; }
}
