using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SACCOBlockChainSystem.Models
{
    [Index(nameof(SharesCode), nameof(CompanyCode), IsUnique = true)]
    public partial class Sharetype
    {
        [Key]
        public string SharesCode { get; set; } = null!; // must not be the same in the same company but can be the same in different company,

        public string? SharesType { get; set; }

        public string SharesAcc { get; set; } = null!;

        public int? PlacePeriod { get; set; }

        public float? LoanToShareRatio { get; set; }

        public int? Issharecapital { get; set; }

        public decimal? Interest { get; set; }
        public string CompanyCode { get; set; } = null!;

        public decimal? MaxAmount { get; set; }

        public string? Guarantor { get; set; }

        public string? AuditId { get; set; }

        public DateTime? AuditTime { get; set; }

        public string? Accno { get; set; }

        public string? Shareboost { get; set; }

        public bool IsMainShares { get; set; }

        public bool UsedToGuarantee { get; set; } // check if it is true to loanapplic

        public string? ContraAcc { get; set; }

        public bool UsedToOffset { get; set; } // check if it is true to loanapplic and offset loan

        public bool Withdrawable { get; set; } // check if it is true to loanapplic

        public bool Loanquaranto { get; set; } // check if it is true to loanapplic

        public int Priority { get; set; }

        public decimal MinAmount { get; set; }

        public string? Ppacc { get; set; }

        public decimal LowerLimit { get; set; }

        public decimal ElseRatio { get; set; }

        public DateTime? AuditDateTime { get; set; }

        [StringLength(255)]
        public string? BlockchainTxId { get; set; }

        public virtual ICollection<ContribShare> ContribShares { get; set; } = new List<ContribShare>();

        public virtual ICollection<Contrib> Contribs { get; set; } = new List<Contrib>();
    }
}

