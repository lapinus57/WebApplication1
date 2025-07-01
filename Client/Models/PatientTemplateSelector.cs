using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Client.Models
{
    public class PatientTemplateSelector : DataTemplateSelector
    {
        public DataTemplate? DefaultTemplate { get; set; }
        public DataTemplate? TakenTemplate { get; set; }

        protected override DataTemplate? SelectTemplateCore(object item)
        {
            if (item is Patient patient)
                return patient.IsTaken ? TakenTemplate : DefaultTemplate;
            return base.SelectTemplateCore(item);
        }
    }
}
