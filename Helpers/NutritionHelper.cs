namespace Syracuse;

public record Cpfc(int Calories, int Proteins, int Fats, int Cabs);

public record Diet(int[] Breakfast, int[] Snack1, int[] Lunch, int[] Snack2, int[] Dinner);

public static class NutritionHelper
{
    // rows: calories, proteins, fats, carbohydrates
    // columns: porridge, proteins, nuts, chocolate, 
    public static float[,] Meals =
    {
        { 330f, 62f, 13f, 3f }, //0
        { 162f, 0f, 20f, 5f }, //1
        { 600f, 22f, 18f, 48f }, //2
        { 460f, 25f, 10f, 36f } //3
    };

    public static Cpfc CalculateCpfc(Agenda data)
    {
        float proteins = default, fats = default, cabs = default, calories = default, basalCal = default;
        var purpose = (float)data.Purpouse.AsFloat();
        var dailyActivity = (float)data.DailyActivity.AsFloat();

        switch (data.Gender)
        {
            case "Мужчина":
                proteins = (float)(data!.Weight * 2f);
                fats = (float)(data.Weight * 1f);
                basalCal = (float)((10f * data.Weight + 6.25f * data.Height - 5f * data.Age + 5f) * dailyActivity);
                calories = basalCal * purpose;
                cabs = (calories - proteins * 4f - fats * 9f) / 4f;
                break;
            case "Женщина":
                proteins = (float)(data.Weight * 1.7f);
                fats = (float)(data.Weight * 1.2f);
                basalCal = (float)((10f * data.Weight + 6.25f * data.Height - 5f * data.Age - 161f) * dailyActivity);
                calories = basalCal * purpose;
                cabs = (calories - proteins * 4f - fats * 9f) / 4f;
                break;
        }

        return new Cpfc((int)Math.Round(calories), (int)Math.Round(proteins), (int)Math.Round(fats),
            (int)Math.Round(cabs));
    }

    public static Diet CalculateDiet(Cpfc data, string gender)
    {
        var isMale = gender == "Мужчина";
        int[] breakfast, snack1, lunch, snack2, dinner;
        float porridge, proteins, nuts, chocolate, eggs;

        //ЗАВТРАК
        porridge = (data.Calories - data.Fats * 9 - data.Proteins * 4) * (isMale ? 0.4f : 0.6f) * 100f / Meals[0, 0];
        eggs = (data.Calories - data.Cabs * 4f - data.Fats * 9f) * 100f / Meals[1, 0] * 0.0045f;
        proteins = 0f;
        nuts = 0f;
        chocolate = 0f;
        breakfast = new[]
        {
            (int)Math.Round(porridge), (int)Math.Round(proteins), (int)Math.Round(nuts), (int)Math.Round(chocolate),
            (int)Math.Round(eggs)
        };

        //ПЕРЕКУС 1
        nuts = (data.Calories - data.Proteins * 4 - data.Cabs * 4) * 0.6f * 100f / Meals[2, 0];
        chocolate = (data.Calories - data.Proteins * 4 - data.Cabs * 4) * 0.4f * 100f / Meals[3, 0];
        porridge = 0f;
        proteins = 0f;
        eggs = 0f;
        snack1 = new[]
        {
            (int)Math.Round(porridge), (int)Math.Round(proteins), (int)Math.Round(nuts), (int)Math.Round(chocolate),
            (int)Math.Round(eggs)
        };

        //ОБЕД
        porridge = (data.Calories - data.Fats * 9 - data.Proteins * 4) * (isMale ? 0.3f : 0.4f) * 100f / Meals[0, 0];
        proteins = (data.Calories - data.Cabs * 4f - data.Fats * 9f) * 100f / Meals[1, 0] * 0.35f;
        nuts = 0f;
        chocolate = 0f;
        eggs = 0f;
        lunch = new[]
        {
            (int)Math.Round(porridge), (int)Math.Round(proteins), (int)Math.Round(nuts), (int)Math.Round(chocolate),
            (int)Math.Round(eggs)
        };

        //ПЕРЕКУС 2
        eggs = (data.Calories - data.Cabs * 4f - data.Fats * 9f) * 100f / Meals[1, 0] * 0.0045f;
        proteins = 0f;
        porridge = 0f;
        nuts = 0f;
        chocolate = 0f;
        snack2 = new[]
        {
            (int)Math.Round(porridge), (int)Math.Round(proteins), (int)Math.Round(nuts), (int)Math.Round(chocolate),
            (int)Math.Round(eggs)
        };

        //УЖИН
        porridge = isMale
            ? (data.Calories - data.Fats * 9 - data.Proteins * 4) * (isMale ? 0.3f : 0.4f) * 100f / Meals[0, 0]
            : 0f;
        proteins = (data.Calories - data.Cabs * 4f - data.Fats * 9f) * 100f / Meals[1, 0] * 0.35f;
        nuts = 0f;
        chocolate = 0f;
        eggs = 0f;
        dinner = new[]
        {
            (int)Math.Round(porridge), (int)Math.Round(proteins), (int)Math.Round(nuts), (int)Math.Round(chocolate),
            (int)Math.Round(eggs)
        };

        return new Diet(breakfast, snack1, lunch, snack2, dinner);
    }
}