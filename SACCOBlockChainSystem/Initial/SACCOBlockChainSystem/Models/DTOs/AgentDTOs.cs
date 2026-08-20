// Models/DTOs/AgentDTOs.cs - Updated
using System;
using System.ComponentModel.DataAnnotations;

namespace SACCOBlockChainSystem.Models.DTOs
{
    public class AgentDTO
    {
        [Required(ErrorMessage = "ID Number is required")]
        [StringLength(50)]
        [Display(Name = "ID Number")]
        public string? IdNo { get; set; }

        [StringLength(50)]
        [Display(Name = "Recruitment Agent Type")]
        public string? RecruitementAgents { get; set; }  // Dropdown: Staff, Agent, Agri-prenour, Board member

        [Required(ErrorMessage = "Names are required")]
        [StringLength(150)]
        [Display(Name = "Full Names")]
        public string? Names { get; set; }

        [Required(ErrorMessage = "Gender is required")]
        [StringLength(50)]
        [Display(Name = "Gender")]
        public string? Gender { get; set; }

        [StringLength(50)]
        [Display(Name = "Staff Code")]
        public string? StaffCode { get; set; }

        [StringLength(50)]
        [Display(Name = "Occupation")]
        public string? Occupation { get; set; }

        [Required(ErrorMessage = "Land Phone is required")]
        [StringLength(50)]
        [Display(Name = "Land Phone")]
        public string? LandPhone { get; set; }

        [Required(ErrorMessage = "Mobile Number is required")]
        [StringLength(50)]
        [Display(Name = "Mobile Number")]
        public string? MobileNo { get; set; }

        [StringLength(50)]
        [Display(Name = "Branch Name")]
        public string? Branchname { get; set; }

        [Required(ErrorMessage = "Home Address is required")]
        [StringLength(50)]
        [Display(Name = "Home Address")]
        public string? HomeAddress { get; set; }

        [Required(ErrorMessage = "Town is required")]
        [StringLength(50)]
        [Display(Name = "Town")]
        public string? Town { get; set; }

        [Required]
        [DataType(DataType.Date)]
        [Display(Name = "Recruitment Date")]
        public DateTime Recruitdate { get; set; } = DateTime.Now;

        [StringLength(100)]
        [Display(Name = "PIN")]
        public string? PIN { get; set; }

        public string? CompanyCode { get; set; }
        public string? CreatedBy { get; set; }  // This will be the logged-in user for AuditId
    }

    // Response DTO for returning agent data
    public class AgentResponseDTO
    {
        public long Id { get; set; }
        public string? IdNo { get; set; }
        public string? RecruitementAgents { get; set; }
        public string? Names { get; set; }
        public string? Gender { get; set; }
        public string? StaffCode { get; set; }
        public string? Occupation { get; set; }
        public string? LandPhone { get; set; }
        public string? MobileNo { get; set; }
        public string? Branchname { get; set; }
        public string? CompanyCode { get; set; }
        public string? HomeAddress { get; set; }
        public string? Town { get; set; }
        public DateTime Recruitdate { get; set; }
        public string? PIN { get; set; }
        public string? BlockchainTxId { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? CreatedBy { get; set; }  // AuditId
    }

    // Simple DTO for dropdown lists
    public class AgentSimpleDTO
    {
        public long Id { get; set; }
        public string? IdNo { get; set; }
        public string? Names { get; set; }
        public string? MobileNo { get; set; }
    }
}