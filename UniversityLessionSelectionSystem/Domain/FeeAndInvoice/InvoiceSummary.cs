using System.Collections.Generic; 

namespace UniversityLessonSelectionSystem.Domain.FeeAndInvoice
{
    public sealed class InvoiceSummary
    {
        public IList<InvoiceItem> Items { get; set; } = new List<InvoiceItem>();
        public decimal Subtotal { get; set; }
        public decimal Tax { get; set; }
        public decimal Total { get; set; }
    }
}
