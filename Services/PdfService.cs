using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;
using SixLabors.ImageSharp;
using static UglyToad.PdfPig.Writer.PdfDocumentBuilder;

namespace Syracuse;

public interface IPdfService
{
    void CreateNutrition(string path, SaleType type, Agenda agenda, Cpfc cpfc, Diet diet);
    //string CreateWorkoutProgram();
}

public class PdfService : IPdfService
{
    private ILogger<PdfService> _logger;
    private bool isInit = false;

    private static readonly string s_fontPath = Path.Combine("Resources", "Fonts", "AlumniSans-Regular.ttf");
    private static readonly string s_standartNutritionTemplatePath = Path.Combine("Resources", "Templates", "nutrition-template-standart.png");
    private static readonly string s_proNutritionTemplatePath = Path.Combine("Resources", "Templates", "nutrition-template-pro.png");

    private AddedFont _font;
    private byte[] _nutritionRawTemplate;
    private PdfDocumentBuilder _builder;

    private int _nutritionWidth;
    private int _nutritionHeight;

    public PdfService(ILogger<PdfService> logger) => _logger = logger;

    private void Init(SaleType type)
    {
        try
        {
            _nutritionRawTemplate = type switch
            {
                SaleType.Standart => File.ReadAllBytes(s_standartNutritionTemplatePath),
                SaleType.Pro => File.ReadAllBytes(s_proNutritionTemplatePath),
                _ => throw new PdfExсeption("Отстутсвует шаблон для указанного письма")
            };

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
        isInit = true;
    }

    public void CreateNutrition(string path, SaleType type, Agenda agenda, Cpfc cpfc, Diet diet)
    {
        if (isInit == false) Init(type);

        try
        {
            var page = _builder.AddPage(_nutritionWidth, _nutritionHeight);
            page.AddPng(_nutritionRawTemplate, page.PageSize);
            page.SetTextAndFillColor(255, 255, 255);

            AddText(page, Label.CreateAge((int)agenda.Age, 239, 1045));
            AddText(page, Label.CreateHeight((int)agenda.Height, 567, 1045));
            AddText(page, Label.CreateWeight((int)agenda.Weight, 896, 1045));
            AddText(page, Label.CreateText(agenda.Purpouse.AsString(), 1226, 1045));

            AddText(page, Label.CreatePfc(cpfc.Proteins, 239, 1473));
            AddText(page, Label.CreatePfc(cpfc.Fats, 567, 1473));
            AddText(page, Label.CreatePfc(cpfc.Cabs, 896, 1473));
            AddText(page, Label.CreateText(cpfc.Calories.ToString(), 1226, 1473));

            AddText(page, Label.CreateText($"Каша {diet.Breakfast[0]}гр. + яйца {diet.Breakfast[4]}шт.", 2328, 1035));
            AddText(page, Label.CreateText($"Орехи {diet.Snack1[2]}гр. + шоколад {diet.Snack1[3]}гр.", 2328, 1154));
            AddText(page, Label.CreateText($"Каша {diet.Lunch[0]}гр. + белки {diet.Lunch[1]}гр.", 2328, 1274));
            AddText(page, Label.CreateText($"Яйца {diet.Snack2[4]}шт.", 2328, 1393));
            AddText(page, Label.CreateText($"Белки {diet.Dinner[1]}гр.", 2328, 1512));

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
            70,
            new PdfPoint(data.Position.x, 2150 - data.Position.y),
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