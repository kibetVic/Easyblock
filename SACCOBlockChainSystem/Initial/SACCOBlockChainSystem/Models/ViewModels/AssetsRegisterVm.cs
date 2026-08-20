// Models/ViewModels/AssetsRegisterVm.cs
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace SACCOBlockChainSystem.Models.ViewModels
{
    public class AssetsRegisterVm
    {
        public long? ID { get; set; }

        [Display(Name = "Class")]
        [StringLength(50)]
        public string? Class { get; set; }

        [Required(ErrorMessage = "Asset Type is required")]
        [Display(Name = "Asset Type")]
        [StringLength(50)]
        public string? AssetType { get; set; }

        [Required(ErrorMessage = "Asset Name is required")]
        [Display(Name = "Asset Name")]
        [StringLength(50)]
        public string? AssetName { get; set; }

        [Display(Name = "Tag Number")]
        [StringLength(50)]
        public string? TagNo { get; set; }

        [Display(Name = "Serial Number")]
        [StringLength(50)]
        public string? SerialNo { get; set; }

        [Display(Name = "Quantity")]
        [Range(1, 999999999, ErrorMessage = "Please enter a valid whole number")]
        public int? Quantity { get; set; }

        [Display(Name = "Actual Value")]
        [Range(0, 9999999999.99, ErrorMessage = "Please enter a valid amount")]
        public decimal? ActualValue { get; set; }

        [Display(Name = "Market Value")]
        [Range(0, 9999999999.99, ErrorMessage = "Please enter a valid amount")]
        public decimal? MarketValue { get; set; }

        [Display(Name = "Total Value")]
        public decimal? TotalValue { get; set; }

        [Display(Name = "Date of Manufacture")]
        [DataType(DataType.Date)]
        public DateTime? DateOfManufacture { get; set; }

        [Display(Name = "Date Purchased")]
        [DataType(DataType.Date)]
        public DateTime? DatePurchased { get; set; }

        [Display(Name = "Transaction Number")]
        [StringLength(50)]
        public string? TransactionNo { get; set; }

        [Required(ErrorMessage = "Company Code is required")]
        [Display(Name = "Company")]
        [StringLength(50)]
        public string? CompanyCode { get; set; }

        [Display(Name = "Location")]
        [StringLength(50)]
        public string? Location { get; set; }

        [Display(Name = "Posted")]
        public bool? posted { get; set; }
        public string? AuditId { get; set; }
        public DateTime? AuditTime { get; set; }

        // DROPDOWN LISTS - These belong here, NOT in the entity model
        //public List<SelectListItem> AssetTypeList { get; set; }
        public List<SelectListItem> ClassList { get; set; }
        public List<SelectListItem> CompanyList { get; set; }
        //public List<SelectListItem> LocationList { get; set; }
    }
}