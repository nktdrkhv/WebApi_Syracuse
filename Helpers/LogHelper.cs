﻿using System.Text;

namespace Syracuse;

public static class LogHelper
{

    public static string RawData(Dictionary<string, string> data)
    {
        var log = new StringBuilder();
        _ = log.AppendLine("Raw input data:");
        _ = log.AppendLine(DateTime.UtcNow.ToString());
        foreach (var input in data)
        {
            if (string.Equals(input.Key, "file"))
                continue;
            _ = log.AppendLine($"{input.Key} - {input.Value}");
        }
        log[^1] = ' ';
        return log.ToString();
    }

    public static string ClientInfo(Client client, Agenda? agenda)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Информация о клиенте:")
            .AppendLine($"Имя: {client.Name}")
            .AppendLine($"Почта: {client.Email}")
            .AppendLine($"Телефон: {client.Phone}");

        if (agenda is not null)
        {
            sb.AppendLine("\nДанные клиента:");
            if (agenda.Age is not null) sb.AppendLine($"Возраст: {agenda.Age}");
            if (agenda.Gender is not null) sb.AppendLine($"Пол: {agenda.Gender}");
            if (agenda.Age is not null) sb.AppendLine($"Возраст: {agenda.Age}");
            if (agenda.Height is not null) sb.AppendLine($"Рост: {agenda.Height}");
            if (agenda.Weight is not null) sb.AppendLine($"Вес: {agenda.Weight}");
            if (agenda.ActivityLevel is not null) sb.AppendLine($"Образ жизни: {agenda.ActivityLevel.AsString()}");
            if (agenda.DailyActivity is not null) sb.AppendLine($"Количество тренировок: {agenda.DailyActivity.AsString()}");
            if (agenda.Purpouse is not null) sb.AppendLine($"Цель: {agenda.Purpouse.AsString()}");
            if (agenda.Focus is not null) sb.AppendLine($"Акцент на: {agenda.Focus.AsString()}");
            if (agenda.Diseases is not null) sb.AppendLine($"Заболевания: {agenda.Diseases}");
            if (agenda.Trainer is not null) sb.AppendLine($"Тренер: {agenda.Trainer}");
        }

        return sb.ToString();
    }

    public static string ClientInfo(Client client) => ClientInfo(client, null);
}
