namespace Syracuse;

public static class MatchHelper
{
    public static Dictionary<string, string> TransformToValues(Sale sale)
    {
        Dictionary<string, string> valueDic;
        switch (sale.Type)
        {
            case SaleType.Begginer:
                valueDic = new() { ["key"] = sale.Key, };
                break;
            case SaleType.Profi:
                valueDic = new()
                {
                    ["key"] = sale.Key,
                    ["name"] = sale.Client.Name,
                    ["phone"] = sale.Client.Phone,
                    ["email"] = sale.Client.Email,
                    ["gender"] = sale.Agenda.Gender.AsValue().ToString(),
                    ["activity_level"] = sale.Agenda.ActivityLevel.ToString(),
                    ["focus"] = sale.Agenda.Focus.ToString(),
                    ["purpouse"] = sale.Agenda.Purpouse.ToString(),
                    ["diseases"] = sale.Agenda.Diseases,
                };
                break;
            case SaleType.Standart:
                valueDic = new() { ["key"] = sale.Key, };
                break;
            case SaleType.Pro:
                valueDic = new()
                {
                    ["key"] = sale.Key,
                    ["name"] = sale.Client.Name,
                    ["phone"] = sale.Client.Phone,
                    ["email"] = sale.Client.Email,
                    ["phone"] = sale.Agenda.Gender.AsValue().ToString(),
                    ["phone"] = sale.Agenda.Age.ToString(),
                    ["phone"] = sale.Agenda.Height.ToString(),
                    ["phone"] = sale.Agenda.Weight.ToString(),
                    ["phone"] = sale.Agenda.DailyActivity.ToString(),
                    ["phone"] = sale.Agenda.Purpouse.ToString(),
                };
                break;
            case SaleType.Coach:
                valueDic = new()
                {
                    ["key"] = sale.Key,
                    ["name"] = sale.Client.Name,
                    ["phone"] = sale.Client.Phone,
                    ["email"] = sale.Client.Email,
                    ["phone"] = sale.Agenda.Gender.AsValue().ToString(),
                    ["phone"] = sale.Agenda.Age.ToString(),
                    ["phone"] = sale.Agenda.Height.ToString(),
                    ["phone"] = sale.Agenda.Weight.ToString(),
                    ["phone"] = sale.Agenda.DailyActivity.ToString(),
                    ["phone"] = sale.Agenda.ActivityLevel.ToString(),
                    ["phone"] = sale.Agenda.Focus.ToString(),
                    ["phone"] = sale.Agenda.Purpouse.ToString(),
                    ["phone"] = sale.Agenda.Diseases,
                };
                break;
            case SaleType.WorkoutProgram:
                valueDic = new()
                {
                    ["key"] = sale.Key,
                    ["gender"] = sale.Agenda.Gender.AsValue().ToString(),
                    ["activity_level"] = sale.Agenda.ActivityLevel.ToString(),
                    ["focus"] = sale.Agenda.Focus.ToString(),
                    ["purpouse"] = sale.Agenda.Purpouse.ToString(),
                    ["diseases"] = sale.Agenda.Diseases,
                };
                break;
        }
        return null;
    }

    // --------------------------------------------------------------------------------

    public static T2 Key<T1, T2>(this Dictionary<T1, T2> dictionary, T1 key) => dictionary.TryGetValue(key, out var value) ? value : default(T2);
    public static bool? AsBool(this int? value) => Archive_ValueToBool.Key(value);
    public static int? AsInt(this string? str) => int.TryParse(str, out int val) ? val : null;
    public static int? AsInt(this int? value) => Archive_ValueToInt.Key(value);
    public static float? AsFloat(this int? value) => Archive_ValueToFloat.Key(value);
    public static string? AsString(this int? value) => Archive_ValueToString.Key(value);
    public static int? AsValue(this string? form) => Archive_FormToValue.Key(form);
    public static string? AsErrorTitle(this SaleType type) => Archive_SaleTypeToErrorTitle.Key(type);
    public static string? AsReinputLink(this SaleType type) => Archive_SaleTypeToYandexFormForReinput.Key(type);

    // --------------------------------------------------------------------------------

    private static readonly Dictionary<int?, bool?> Archive_ValueToBool = new()
    {
        [22] = true,
        [23] = false,
    };

    private static readonly Dictionary<int?, int?> Archive_ValueToInt = new()
    {
        [18] = 2,
        [19] = 3,
        [20] = 4,
        [21] = 5,
    };

    private static readonly Dictionary<int?, float?> Archive_ValueToFloat = new()
    {
        [1] = 0.9f,
        [2] = 1f,
        [3] = 1.2f,

        [4] = 1.2f,
        [5] = 1.375f,
        [6] = 1.55f,
        [7] = 1.725f,
        [8] = 1.9f,
    };

    private static readonly Dictionary<int?, string?> Archive_ValueToString = new()
    {
        [1] = "Похудение",
        [2] = "Поддержание",
        [3] = "Набор",

        [4] = "Малоподвижный образ жизни",
        [5] = "Низкая активность",
        [6] = "Умеренная активность",
        [7] = "Высокая активность",
        [8] = "Предельная активность",

        [9] = "Плечи",
        [10] = "Спина",
        [11] = "Ягодицы",
        [12] = "Ноги",

        [13] = "Мужчина",
        [14] = "Женщина",

        [15] = "Алексей",
        [16] = "Дмитрий",
        [17] = "Мария",
    };

    private static readonly Dictionary<string?, int?> Archive_FormToValue = new()
    {
        ["Похудение"] = 1,
        ["Поддержание"] = 2,
        ["Набор"] = 3,
        ["https://static.tildacdn.info/tild6236-3236-4732-a434-656636626630/photo.svg"] = 1,
        ["https://static.tildacdn.info/tild3065-6233-4963-b833-613334333637/photo.svg"] = 2,
        ["https://static.tildacdn.info/tild6263-6632-4235-b537-333561333539/photo.svg"] = 3,

        ["Малоподвижный образ жизни"] = 4,
        ["Низкая активность"] = 5,
        ["Умеренная активность"] = 6,
        ["Высокая активность"] = 7,
        ["Предельная активность"] = 8,
        ["https://static.tildacdn.info/tild6537-3335-4762-b236-653430616362/__.svg"] = 4,
        ["https://static.tildacdn.info/tild6263-3961-4663-b435-326333613432/__1-2__.svg"] = 5,
        ["https://static.tildacdn.info/tild6132-6366-4936-b736-616531383834/__1-2__-1.svg"] = 6,
        ["https://static.tildacdn.info/tild3264-6662-4430-a236-333638356138/__1-2__-4.svg"] = 7,
        ["https://static.tildacdn.info/tild3030-6337-4631-b735-626566386537/__1-2__-2.svg"] = 8,

        ["Плечи"] = 9,
        ["Спина"] = 10,
        ["Ягодицы"] = 11,
        ["Ноги"] = 12,
        ["https://static.tildacdn.info/tild3835-3566-4137-b462-343935626435/photo.svg"] = 9,
        ["https://static.tildacdn.info/tild3061-3565-4166-a436-323438646266/photo.svg"] = 10,
        ["https://static.tildacdn.info/tild3366-6235-4461-a164-346131656538/photo.svg"] = 11,
        ["https://static.tildacdn.info/tild3766-3536-4931-a437-333134626330/photo.svg"] = 12,

        ["Мужчина"] = 13,
        ["Женщина"] = 14,
        ["https://static.tildacdn.info/tild3661-3562-4133-a230-616233613238/man.svg"] = 13,
        ["https://static.tildacdn.info/tild6165-6563-4335-a361-333038333538/woman.svg"] = 14,

        ["Алексей"] = 15,
        ["Дмитрий"] = 16,
        ["Мария"] = 17,
        ["https://static.tildacdn.info/tild6564-3433-4636-b332-633733323564/__.svg"] = 15,
        ["https://static.tildacdn.info/tild3733-6639-4431-b266-323130303466/__-2.svg"] = 16,
        ["https://static.tildacdn.info/tild3532-3132-4262-a663-363463613838/__-1.svg"] = 17,

        ["2"] = 18,
        ["3"] = 19,
        ["4"] = 20,
        ["5"] = 21,
        ["https://static.tildacdn.info/tild3438-3638-4661-a137-383266373862/2.svg"] = 18,
        ["https://static.tildacdn.info/tild3131-3036-4637-b561-393365626135/3.svg"] = 19,
        ["https://static.tildacdn.info/tild6566-6333-4366-b838-633263336164/4.svg"] = 20,
        ["https://static.tildacdn.info/tild6331-6233-4933-b365-343363643138/5.svg"] = 21,

        ["Да"] = 22,
        ["Нет"] = 23,
    };

    private static readonly Dictionary<SaleType, string?> Archive_SaleTypeToErrorTitle = new()
    {
        [SaleType.Coach] = "Ошибка: запись к Online-тренеру",
        [SaleType.Standart] = "Ошибка: Standart питание. КБЖУ + рацион",
        [SaleType.Pro] = "Ошибка: PRO питание + книга рецептов",
        [SaleType.Begginer] = "Ошибка: программа тренировок для новичков",
        [SaleType.Profi] = "Ошибка: программа тренировок для профессионалов",
        [SaleType.Posing] = "Ошибка: уроки позинга Fitness Bikini",
        [SaleType.Endo] = "Ошибка: запись на консультацию к эндокринологу",
    };

    private static readonly Dictionary<SaleType, string?> Archive_SaleTypeToYandexFormForReinput = new()
    {
        [SaleType.Coach] = "https://forms.yandex.ru/cloud/62ffa34019f03a8bfd90ecb3",
        [SaleType.Standart] = "https://forms.yandex.ru/cloud/62ffae527e794d2d96b13687",
        [SaleType.Pro] = "https://forms.yandex.ru/cloud/62ffae527e794d2d96b13687",
        [SaleType.Begginer] = "https://forms.yandex.ru/cloud/62ffe07d0170aca2958f5c0c",
        [SaleType.Profi] = "https://forms.yandex.ru/cloud/62ffe07d0170aca2958f5c0c",
        [SaleType.WorkoutProgram] = "https://forms.yandex.ru/cloud/62fff1f4d2a4c7ac2baeaa93",
    };
}
