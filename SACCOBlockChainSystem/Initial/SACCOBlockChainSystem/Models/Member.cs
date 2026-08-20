using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace SACCOBlockChainSystem.Models
{
    public partial class Member
    {
        public int Id { get; set; }
        public string MemberNo { get; set; } = null!;

        public string? StaffNo { get; set; }

        public string? Idno { get; set; }

        public string? Surname { get; set; }

        public string? OtherNames { get; set; }

        public string? Sex { get; set; }

        public DateTime? Dob { get; set; }

        public string? Employer { get; set; }

        public string? Dept { get; set; }

        public string? Rank { get; set; }

        public string? Terms { get; set; }

        public string? PresentAddr { get; set; }

        public string? OfficeTelNo { get; set; }

        public string? HomeAddr { get; set; }

        public string? HomeTelNo { get; set; }

        public decimal? RegFee { get; set; }

        public decimal? InitShares { get; set; }

        public DateTime? AsAtDate { get; set; }

        public double? MonthlyContr { get; set; }

        public DateTime? ApplicDate { get; set; }

        public DateTime? EffectDate { get; set; }

        public string? Signed { get; set; }

        public string? Accepted { get; set; }

        public bool? Archived { get; set; }

        public bool? Withdrawn { get; set; }

        public string? Province { get; set; }

        public string? District { get; set; }

        public string? Station { get; set; }

        public string? CompanyCode { get; set; }

        public string? Cigcode { get; set; }

        public string? Pin { get; set; }

        public string? Photo { get; set; }

        public decimal? ShareCap { get; set; }

        public string? BankCode { get; set; }

        public string? Bname { get; set; }

        public string? AuditId { get; set; }

        public DateTime? AuditTime { get; set; }

        public DateTime? EDate { get; set; }

        public string? Posted { get; set; }

        public decimal? InitsharesTransfered { get; set; }

        public DateTime? Transferdate { get; set; }

        public decimal? LoanBalance { get; set; }

        public decimal? InterestBalance { get; set; }

        public string? FormFilled { get; set; }

        public string? EmailAddress { get; set; }

        public string? Accno { get; set; }

        public DateTime? Memberwitrawaldate { get; set; }

        public int? Dormant { get; set; }

        public string? MemberDescription { get; set; }

        public string? Email { get; set; }

        public string? TransactionNo { get; set; }

        public string? MobileNo { get; set; }

        public string? AgentId { get; set; }

        public string? PhoneNo { get; set; }

        public string? Entrance { get; set; }

        public short? Status { get; set; }

        public bool? Mstatus { get; set; }

        public string? MembershipType { get; set; }

        public int? Age { get; set; }

        public string? ApiKey { get; set; }

        public string? UserName { get; set; }
        public int? Run { get; set; }

        public byte[]? ProfilePicture { get; set; }

        public string? ProfileString { get; set; }

        public DateTime? AuditDateTime { get; set; }
        public string? BlockchainTxId { get; set; }

        [NotMapped]
        public string? FullName { get; set; }
        public virtual ICollection<NextOfKeen> NextOfKeens { get; set; } = new List<NextOfKeen>();
        //public virtual ICollection<Endmain> Endmain { get; set; } = new List<Endmain>();
    }
}

