using System.Collections.Generic;

namespace TableauToPbi.Models
{
    public enum ConversionStatus
    {
        Success,
        PartialConversion,
        ManualRequired,
        NotConverted
    }

    public class ConversionResult
    {
        public string DaxExpression { get; set; } = string.Empty;
        public ConversionStatus Status { get; set; } = ConversionStatus.Success;
        public List<string> Warnings { get; set; } = new();
        public List<string> Notes { get; set; } = new();

        public string StatusLabel => Status switch
        {
            ConversionStatus.Success => "✔ Converted",
            ConversionStatus.PartialConversion => "⚠ Partial",
            ConversionStatus.ManualRequired => "✖ Manual Required",
            ConversionStatus.NotConverted => "— Not Converted",
            _ => ""
        };

        public string StatusColor => Status switch
        {
            ConversionStatus.Success => "#107C10",
            ConversionStatus.PartialConversion => "#C19A00",
            ConversionStatus.ManualRequired => "#D83B01",
            _ => "#888888"
        };
    }
}
