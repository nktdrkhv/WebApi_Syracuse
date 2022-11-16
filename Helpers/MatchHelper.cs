using System.Text;

namespace Syracuse;

public static class MatchHelper
{
    public static Dictionary<string?, string?> TransformToValues(Sale sale) => TransformToValues(sale, sale.Type);
    public static Dictionary<string?, string?> TransformToValues(Sale sale, SaleType type)
    {
        Dictionary<string?, string?> valueDic = null;
        switch (type)
        {
            case SaleType.Begginer:
                valueDic = new()
                {
                    ["key"] = sale.Key,
                    ["name"] = sale.Client.Name,
                    ["phone"] = sale.Client.Phone,
                    ["email"] = sale.Client.Email,
                    ["gender"] = sale.Agenda.Gender?.AsValue().ToString(),
                    ["activity_level"] = sale.Agenda.ActivityLevel.ToString(),
                    ["purpouse"] = sale.Agenda.Purpouse.ToString(),
                };
                break;
            case SaleType.Profi:
                valueDic = new()
                {
                    ["key"] = sale.Key,
                    ["name"] = sale.Client.Name,
                    ["phone"] = sale.Client.Phone,
                    ["email"] = sale.Client.Email,
                    ["gender"] = sale.Agenda.Gender?.AsValue().ToString(),
                    ["activity_level"] = sale.Agenda.ActivityLevel.ToString(),
                    ["focus"] = sale.Agenda.Focus.ToString(),
                    ["purpouse"] = sale.Agenda.Purpouse.ToString(),
                    ["diseases"] = sale.Agenda.Diseases,
                };
                break;
            case SaleType.Pro or SaleType.Standart:
                valueDic = new()
                {
                    ["key"] = sale.Key,
                    ["name"] = sale.Client.Name,
                    ["phone"] = sale.Client.Phone,
                    ["email"] = sale.Client.Email,
                    ["gender"] = sale.Agenda.Gender?.AsValue().ToString(),
                    ["age"] = sale.Agenda.Age.ToString(),
                    ["height"] = sale.Agenda.Height.ToString(),
                    ["weight"] = sale.Agenda.Weight.ToString(),
                    ["daily_activity"] = sale.Agenda.DailyActivity.ToString(),
                    ["purpouse"] = sale.Agenda.Purpouse.ToString(),
                };
                break;
            case SaleType.Coach:
                valueDic = new()
                {
                    ["key"] = sale.Key,
                    ["name"] = sale.Client.Name,
                    ["phone"] = sale.Client.Phone,
                    ["email"] = sale.Client.Email,
                    ["gender"] = sale.Agenda.Gender?.AsValue().ToString(),
                    ["age"] = sale.Agenda.Age.ToString(),
                    ["height"] = sale.Agenda.Height.ToString(),
                    ["weight"] = sale.Agenda.Weight.ToString(),
                    ["daily_activity"] = sale.Agenda.DailyActivity.ToString(),
                    ["activity_level"] = sale.Agenda.ActivityLevel.ToString(),
                    ["focus"] = sale.Agenda.Focus.ToString(),
                    ["purpouse"] = sale.Agenda.Purpouse.ToString(),
                    ["diseases"] = sale.Agenda.Diseases,
                    ["trainer"] = sale.Agenda.Trainer,
                };
                break;
            case SaleType.WorkoutProgram:
                valueDic = new()
                {
                    ["key"] = sale.Key,
                    ["gender"] = sale.Agenda.Gender?.AsValue().ToString(),
                    ["activity_level"] = sale.Agenda.ActivityLevel.ToString(),
                    ["focus"] = sale.Agenda.Focus.ToString(),
                    ["purpouse"] = sale.Agenda.Purpouse.ToString(),
                    ["diseases"] = sale.Agenda.Diseases,
                };
                break;
        }
        return valueDic;
    }

    // --------------------------------------------------------------------------------

    public static T2? Key<T1, T2>(this Dictionary<T1, T2> dictionary, T1 key)
    {
        if (key is not null)
            return dictionary.TryGetValue(key, out var value) ? value : default(T2);
        else
            return default(T2);
    }

    public static bool? AsBool(this int? value) => Archive_ValueToBool.Key(value);
    public static int? AsInt(this string? str) => int.TryParse(str, out int val) ? val : null;
    public static int? AsInt(this int? value) => Archive_ValueToInt.Key(value);
    public static float? AsFloat(this int? value) => Archive_ValueToFloat.Key(value);
    public static string? AsString(this int? value) => Archive_ValueToString.Key(value);
    public static string? AsCode(this int? value) => Archive_ValueToCode.Key(value);
    public static int? AsValue(this string? form) => Archive_FormToValue.Key(form);
    public static string? AsErrorTitle(this SaleType type) => Archive_SaleTypeToErrorTitle.Key(type);
    public static string? AsReinputLink(this SaleType type) => Archive_SaleTypeToYandexFormForReinput.Key(type);
    public static string? AsInfoString(this List<Contact> contacts, string separator)
    {
        var sb = new StringBuilder();
        foreach (var contact in contacts)
            sb.AppendLine(contact.Info);
        if (separator is not null) sb.Replace("\n", separator);
        return sb.ToString();
    }

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
        [8] = "Очень высокая активность",

        [9] = "Плечи",
        [10] = "Спина",
        [11] = "Ягодицы",
        [12] = "Ноги",

        [13] = "Мужчина",
        [14] = "Женщина",

        [18] = "2",
        [19] = "3",
        [20] = "4",
        [21] = "5",
    };

    private static readonly Dictionary<int?, string?> Archive_ValueToCode = new()
    {
        [28] = "gender",
        [29] = "age",
        [30] = "height",
        [31] = "weight",
        [32] = "purpouse",
        [33] = "daily_activity",
        [34] = "activity_level",
        [35] = "focus",
        [36] = "trainer",
        [37] = "diseases",
        [38] = "videos"
    };

    private static readonly Dictionary<string?, int?> Archive_FormToValue = new()
    {
        ["Похудение"] = 1,
        ["Поддержание"] = 2,
        ["Набор"] = 3,
        ["https://static.tildacdn.com/tild6236-3236-4732-a434-656636626630/photo.svg"] = 1,
        ["https://static.tildacdn.com/tild3065-6233-4963-b833-613334333637/photo.svg"] = 2,
        ["https://static.tildacdn.com/tild6263-6632-4235-b537-333561333539/photo.svg"] = 3,

        ["Малоподвижный образ жизни"] = 4,
        ["Низкая активность"] = 5,
        ["Умеренная активность"] = 6,
        ["Высокая активность"] = 7,
        ["Очень активные"] = 7,
        ["Очень высокая активность"] = 8,
        ["Предельная активность"] = 8,
        ["https://static.tildacdn.com/tild6537-3335-4762-b236-653430616362/__.svg"] = 4,
        ["https://static.tildacdn.com/tild6263-3961-4663-b435-326333613432/__1-2__.svg"] = 5,
        ["https://static.tildacdn.com/tild6132-6366-4936-b736-616531383834/__1-2__-1.svg"] = 6,
        ["https://static.tildacdn.com/tild3264-6662-4430-a236-333638356138/__1-2__-4.svg"] = 7,
        ["https://static.tildacdn.com/tild3030-6337-4631-b735-626566386537/__1-2__-2.svg"] = 8,

        ["Плечи"] = 9,
        ["Спина"] = 10,
        ["Ягодицы"] = 11,
        ["Ноги"] = 12,
        ["https://static.tildacdn.com/tild3835-3566-4137-b462-343935626435/photo.svg"] = 9,
        ["https://static.tildacdn.com/tild3061-3565-4166-a436-323438646266/photo.svg"] = 10,
        ["https://static.tildacdn.com/tild3366-6235-4461-a164-346131656538/photo.svg"] = 11,
        ["https://static.tildacdn.com/tild3766-3536-4931-a437-333134626330/photo.svg"] = 12,

        ["Мужчина"] = 13,
        ["Женщина"] = 14,
        ["https://static.tildacdn.com/tild3661-3562-4133-a230-616233613238/man.svg"] = 13,
        ["https://static.tildacdn.com/tild6165-6563-4335-a361-333038333538/woman.svg"] = 14,

        ["2"] = 18,
        ["3"] = 19,
        ["4"] = 20,
        ["5"] = 21,
        ["https://static.tildacdn.com/tild3438-3638-4661-a137-383266373862/2.svg"] = 18,
        ["https://static.tildacdn.com/tild3131-3036-4637-b561-393365626135/3.svg"] = 19,
        ["https://static.tildacdn.com/tild6566-6333-4366-b838-633263336164/4.svg"] = 20,
        ["https://static.tildacdn.com/tild6331-6233-4933-b365-343363643138/5.svg"] = 21,

        ["Да"] = 22,
        ["Нет"] = 23,

        ["Почта"] = 25,
        ["Телефон"] = 26,
        ["Адрес"] = 27,

        ["Пол"] = 28,
        ["Возраст"] = 29,
        ["Рост"] = 30,
        ["Вес"] = 31,
        ["Цель тренировок"] = 32,
        ["Еженедельная активность"] = 33,
        ["Уровень активности"] = 34,
        ["Акцент группы мышц"] = 35,
        ["Тренер"] = 36,
        ["Заболевания"] = 37,

        ["Выбранные видео"] = 38,
    };

    private static readonly Dictionary<SaleType?, string?> Archive_SaleTypeToErrorTitle = new()
    {
        [SaleType.Coach] = "Ошибка: запись к Online-тренеру",
        [SaleType.Standart] = "Ошибка: Standart питание. КБЖУ + рацион",
        [SaleType.Pro] = "Ошибка: PRO питание + книга рецептов",
        [SaleType.Begginer] = "Ошибка: программа тренировок для новичков",
        [SaleType.Profi] = "Ошибка: программа тренировок для профессионалов",
        [SaleType.Posing] = "Ошибка: уроки позинга Fitness Bikini",
        [SaleType.Endo] = "Ошибка: запись на консультацию к эндокринологу",
    };

    private static readonly Dictionary<SaleType?, string?> Archive_SaleTypeToYandexFormForReinput = new()
    {
        [SaleType.Coach] = "https://forms.yandex.ru/cloud/62ffa34019f03a8bfd90ecb3",
        [SaleType.Standart] = "https://forms.yandex.ru/cloud/6311d17d18a45f9fb782979e",
        [SaleType.Pro] = "https://forms.yandex.ru/cloud/631364b43e0fc29063e3d915",
        [SaleType.Begginer] = "https://forms.yandex.ru/cloud/6311e6b78738c957e3f1d164",
        [SaleType.Profi] = "https://forms.yandex.ru/cloud/62ffe07d0170aca2958f5c0c",
        [SaleType.WorkoutProgram] = "https://korablev-team.ru/load/wp",
    };
}
