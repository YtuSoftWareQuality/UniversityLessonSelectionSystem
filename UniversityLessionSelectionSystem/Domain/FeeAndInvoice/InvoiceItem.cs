using UniversityLessonSelectionSystem.Domain.Enums;

namespace UniversityLessonSelectionSystem.Domain.FeeAndInvoice
{
    public sealed class InvoiceItem
    {
        public FeeComponentType Component { get; set; }
        public InvoiceKind Kind { get; set; }
        public decimal Amount { get; set; }
        public string Notes { get; set; }
    }
}
