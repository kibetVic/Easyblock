// Member Management JavaScript Functions

// Initialize member search autocomplete
function initMemberAutocomplete(inputSelector, resultsSelector) {
    $(inputSelector).autocomplete({
        source: function (request, response) {
            $.ajax({
                url: '/MemberMvc/SearchMembersAjax',
                data: { searchTerm: request.term },
                success: function (data) {
                    if (data.success) {
                        response($.map(data.data, function (item) {
                            return {
                                label: item.memberNo + ' - ' + item.surname + ' ' + item.otherNames,
                                value: item.memberNo
                            };
                        }));
                    }
                }
            });
        },
        minLength: 2,
        select: function (event, ui) {
            window.location.href = '/MemberMvc/Details/' + ui.item.value;
        }
    });
}

// Generate QR Code for member
function generateMemberQrCode(containerId, memberData) {
    QRCode.toCanvas(document.getElementById(containerId), JSON.stringify(memberData), {
        width: 200,
        margin: 1,
        color: {
            dark: '#000000',
            light: '#FFFFFF'
        }
    });
}

// Print member statement
function printMemberStatement(memberNo, startDate, endDate) {
    var url = `/MemberMvc/PrintStatement?memberNo=${memberNo}`;
    if (startDate) url += `&startDate=${startDate}`;
    if (endDate) url += `&endDate=${endDate}`;

    var printWindow = window.open(url, '_blank');
    printWindow.focus();
    printWindow.print();
}

// Export members to CSV
function exportMembersToCsv(members) {
    var csv = 'MemberNo,Surname,OtherNames,ID,Phone,Email,Company,Status,ShareBalance\n';

    members.forEach(function (member) {
        csv += `"${member.MemberNo}","${member.Surname}","${member.OtherNames}","${member.Idno}","${member.PhoneNo}","${member.Email}","${member.CompanyCode}","${member.Status}","${member.ShareBalance}"\n`;
    });

    var blob = new Blob([csv], { type: 'text/csv' });
    var url = window.URL.createObjectURL(blob);
    var a = document.createElement('a');
    a.href = url;
    a.download = `members_${new Date().toISOString().slice(0, 10)}.csv`;
    a.click();
    window.URL.revokeObjectURL(url);
}

// Validate member form
function validateMemberForm(formId) {
    var form = document.getElementById(formId);
    var isValid = true;
    var errors = [];

    // Check required fields
    $(form).find('[required]').each(function () {
        if (!$(this).val().trim()) {
            isValid = false;
            errors.push($(this).attr('name') + ' is required');
        }
    });

    // Validate phone number
    var phone = $(form).find('[name="PhoneNo"]').val();
    if (phone && !/^[0-9]{9,15}$/.test(phone.replace(/\D/g, ''))) {
        isValid = false;
        errors.push('Invalid phone number format');
    }

    // Validate ID number
    var idNo = $(form).find('[name="IdNo"]').val();
    if (idNo && idNo.length < 6) {
        isValid = false;
        errors.push('ID number must be at least 6 characters');
    }

    if (!isValid) {
        toastr.error(errors.join('<br>'));
    }

    return isValid;
}

// Quick contact member
function quickContactMember(memberNo, phoneNumber) {
    if (confirm(`Call ${phoneNumber}?`)) {
        window.location.href = `tel:${phoneNumber}`;
    }
}

// Load member transactions chart
function loadMemberTransactionsChart(memberNo, containerId) {
    $.ajax({
        url: `/MemberMvc/GetTransactionChartData?memberNo=${memberNo}`,
        success: function (data) {
            if (data.success) {
                var ctx = document.getElementById(containerId).getContext('2d');
                new Chart(ctx, {
                    type: 'line',
                    data: {
                        labels: data.labels,
                        datasets: [{
                            label: 'Transaction Amount',
                            data: data.amounts,
                            borderColor: '#4e73df',
                            backgroundColor: 'rgba(78, 115, 223, 0.05)',
                            fill: true
                        }]
                    },
                    options: {
                        responsive: true,
                        plugins: {
                            legend: {
                                display: false
                            }
                        },
                        scales: {
                            y: {
                                beginAtZero: true,
                                ticks: {
                                    callback: function (value) {
                                        return 'KES ' + value;
                                    }
                                }
                            }
                        }
                    }
                });
            }
        }
    });
}

// Initialize all member-related functionality
$(document).ready(function () {
    // Initialize autocomplete on all member search inputs
    initMemberAutocomplete('.member-search-input', '.search-results');

    // Attach quick contact buttons
    $('.quick-contact-btn').click(function () {
        var memberNo = $(this).data('member');
        var phone = $(this).data('phone');
        quickContactMember(memberNo, phone);
    });

    // Initialize tooltips
    $('[data-toggle="tooltip"]').tooltip();

    // Initialize date pickers
    $('.date-picker').datepicker({
        format: 'yyyy-mm-dd',
        autoclose: true
    });
});