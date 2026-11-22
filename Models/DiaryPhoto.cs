namespace GunlukUygulamasi.Models;

public class DiaryPhoto
{
    public string Path { get; set; } = string.Empty;
    public double X { get; set; } = 40;
    public double Y { get; set; } = 40;
    public double Width { get; set; } = 240;
    public double Height { get; set; } = 170;
    public bool KeepAspectRatio { get; set; } = true;
    public List<DiaryStroke> Strokes { get; set; } = [];
}

public class DiaryStroke
{
    public string Tool { get; set; } = "Pen";
    public string Color { get; set; } = "#FDE047";
    public double Thickness { get; set; } = 4;
    public List<DiaryPoint> Points { get; set; } = [];
}

public class DiaryPoint
{
    public double X { get; set; }
    public double Y { get; set; }
}
