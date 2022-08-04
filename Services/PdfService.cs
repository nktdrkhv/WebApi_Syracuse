using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;
using SixLabors.ImageSharp;
using static UglyToad.PdfPig.Writer.PdfDocumentBuilder;

namespace Syracuse;

public class PdfService
{
    private static string s_fontPath = Path.Combine("Resources", "Fonts", "AlumniSans-Regular.ttf");
    private static string s_templatePath = Path.Combine("Resources", "food-template.png");

    private AddedFont _font;
    private byte[] _rawTemplate;
    private PdfDocumentBuilder _builder;

    private int _width;
    private int _height;

    public PdfService()
    {
        _rawTemplate = File.ReadAllBytes(s_templatePath);

        var img = Image.Load(_rawTemplate);
        _width = img.Width;
        _height = img.Height;
    }

    public string CreatePdf(Customer customer)
    {
        _builder = new PdfDocumentBuilder 
        {
            ArchiveStandard = PdfAStandard.A2A
        };
        _font = _builder.AddTrueTypeFont(File.ReadAllBytes(s_fontPath));

        var page = _builder.AddPage(_width, _height);
        page.AddPng(_rawTemplate, page.PageSize);
        page.SetTextAndFillColor(255, 255, 255);

        AddText(page, Label.CreateAge(customer.Age, 120, 525));
        AddText(page, Label.CreateHeight(customer.Height, 285, 525));
        AddText(page, Label.CreateWeight(customer.Weight, 450, 525));
        AddText(page, Label.CreateText(customer.Purpose, 616, 525));

        var cpfc = CustomerHelper.CalculateCpfc(customer);
        AddText(page, Label.CreatePfc(cpfc.Proteins, 120, 740));
        AddText(page, Label.CreatePfc(cpfc.Fats, 285, 740));
        AddText(page, Label.CreatePfc(cpfc.Cabs, 450, 740));
        AddText(page, Label.CreateText(cpfc.Calories.ToString(), 616, 740));

        var bytes = _builder.Build();
        var path = Path.Combine("Resources", "Temp", Guid.NewGuid().ToString());
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private record Label
    {
        public string Text { get; set; }
        public (int x, int y) Position { get; set; }

        private Label(string text, int posX, int posY)
        {
            Text = text;
            Position = (posX, posY);
        }

        public static Label CreateAge(int age, int posX, int posY)
        {
            string textAge = (age % 10) switch
            {
                0 or 5 or 6 or 7 or 8 or 9 => $"{age} лет",
                1 => $"{age} год",
                2 or 3 or 4 => $"{age} года",
                _ => age.ToString(),
            };
            return new (textAge, posX, posY);
        }

        public static Label CreateHeight(int height, int posX, int posY) => new($"{height} см.", posX, posY);
        public static Label CreateWeight(int weight, int posX, int posY) => new($"{weight} кг.", posX, posY);
        public static Label CreatePfc(int pfc, int posX, int posY) => new($"{pfc} г.", posX, posY);
        public static Label CreateText(string text, int posX, int posY) => new(text, posX, posY);
    }

    private void AddText(PdfPageBuilder page, Label data) => page.AddText(data.Text,
            35,
            new PdfPoint(data.Position.x, 1080 - data.Position.y),
            _font);
}

