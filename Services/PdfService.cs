using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;
using SixLabors.ImageSharp;
using static UglyToad.PdfPig.Writer.PdfDocumentBuilder;

namespace Syracuse;

public interface IPdfService
{
    void CreateNutrition(string path, Agenda agenda, Cpfc cpfc, Diet diet);
    //string CreateWorkoutProgram();
}

public class PdfService : IPdfService
{
    private ILogger<PdfService> _logger;

    private static readonly string s_fontPath = Path.Combine("Resources", "Fonts", "AlumniSans-Regular.ttf");
    private static readonly string s_nutritionTemplatePath = Path.Combine("Resources", "Templates", "nutrition-template.png");

    private AddedFont _font;
    private byte[] _nutritionRawTemplate;
    private PdfDocumentBuilder _builder;

    private int _nutritionWidth;
    private int _nutritionHeight;

    public PdfService(ILogger<PdfService> logger)
    {
        _logger = logger;

        try
        {
            _nutritionRawTemplate = File.ReadAllBytes(s_nutritionTemplatePath);

            var img = Image.Load(_nutritionRawTemplate);
            _nutritionWidth = img.Width;
            _nutritionHeight = img.Height;

            _builder = new PdfDocumentBuilder
            {
                ArchiveStandard = PdfAStandard.A2A
            };
            _font = _builder.AddTrueTypeFont(File.ReadAllBytes(s_fontPath));
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Pdf (ctor): problem with pdf-template loading");
            throw new PdfExсeption("Ошибка во время считывания шаблонов для генерации PDF", ex);
        }
    }

    public void CreateNutrition(string path, Agenda agenda, Cpfc cpfc, Diet diet)
    {
        try
        {
            var page = _builder.AddPage(_nutritionWidth, _nutritionHeight);
            page.AddPng(_nutritionRawTemplate, page.PageSize);
            page.SetTextAndFillColor(255, 255, 255);

            AddText(page, Label.CreateAge((int)agenda.Age, 120, 525));
            AddText(page, Label.CreateHeight((int)agenda.Height, 285, 525));
            AddText(page, Label.CreateWeight((int)agenda.Weight, 450, 525));
            AddText(page, Label.CreateText(agenda.Purpouse.AsString(), 616, 525));

            AddText(page, Label.CreatePfc(cpfc.Proteins, 120, 740));
            AddText(page, Label.CreatePfc(cpfc.Fats, 285, 740));
            AddText(page, Label.CreatePfc(cpfc.Cabs, 450, 740));
            AddText(page, Label.CreateText(cpfc.Calories.ToString(), 616, 740));

            AddText(page, Label.CreateText($"Каша {diet.Breakfast[0]}гр. + любые белки {diet.Breakfast[1]}гр.", 1170, 520));
            AddText(page, Label.CreateText($"Орехи {diet.Snack1[2]}гр. + шоколад {diet.Snack1[3]}гр.", 1170, 580));
            AddText(page, Label.CreateText($"Каша {diet.Lunch[0]}гр. + любые белки {diet.Lunch[1]}гр.", 1170, 640));
            AddText(page, Label.CreateText($"Любые белки {diet.Dinner[1]}гр.", 1170, 700));

            var bytes = _builder.Build();
            File.WriteAllBytes(path, bytes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Pdf (create nutrition): problem with creating nutrition pdf");
            throw new PdfExсeption("Ошибка во время создания программы питания", ex);
        }
    }

    private void AddText(PdfPageBuilder page, Label data) => page.AddText(data.Text,
            35,
            new PdfPoint(data.Position.x, 1080 - data.Position.y),
            _font);

    public record Label
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
            return new(textAge, posX, posY);
        }
        public static Label CreateHeight(int height, int posX, int posY) => new($"{height} см.", posX, posY);
        public static Label CreateWeight(int weight, int posX, int posY) => new($"{weight} кг.", posX, posY);
        public static Label CreatePfc(int pfc, int posX, int posY) => new($"{pfc} г.", posX, posY);
        public static Label CreateText(string text, int posX, int posY) => new(text, posX, posY);
    }
}