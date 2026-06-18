using Ascon.Pilot.SDK;
using System;
using System.Windows;
using System.Xml.Serialization;

namespace StampSMDOExtension.Models
{
    [Serializable]
    [XmlRoot("GraphicLayerElement")]
    public class StampElement : IGraphicLayerElement
    {
        [XmlElement(Order = 0)]
        public Guid ElementId { get; set; }

        [XmlElement(Order = 1)]
        public Guid ContentId { get; set; }

        [XmlElement(Order = 2)]
        public double OffsetX { get; set; }

        [XmlElement(Order = 3)]
        public double OffsetY { get; set; }

        [XmlElement(Order = 4)]
        public double Width { get; set; }

        [XmlElement(Order = 5)]
        public double Height { get; set; }

        [XmlElement(Order = 6)]
        public Point Scale { get; set; }

        [XmlElement(Order = 7)]
        public double Angle { get; set; }

        [XmlElement(Order = 8)]
        public int PositionId { get; set; }

        [XmlElement(Order = 9)]
        public int PageNumber { get; set; }

        [XmlElement(Order = 10)]
        public VerticalAlignment VerticalAlignment { get; set; }

        [XmlElement(Order = 11)]
        public HorizontalAlignment HorizontalAlignment { get; set; }

        [XmlElement(Order = 12)]
        public string ContentType { get; set; }

        [XmlElement(Order = 13)]
        public bool IsFloating { get; set; }

        [XmlElement(Order = 14)]
        public Point CornerPoint { get; set; }
    }
}