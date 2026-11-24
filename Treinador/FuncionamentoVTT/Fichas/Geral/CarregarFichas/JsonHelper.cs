using System;
using UnityEngine;

public static class JsonHelper
{
    // Converte JSON em array de T
    public static T[] FromJson<T>(string json)
    {
        // Se o JSON for nulo ou vazio, retorna array vazio
        if (string.IsNullOrEmpty(json)) return new T[0];

        // Se o JSON começa com '[', precisamos embrulhar ele num objeto para a Unity entender
        if (json.Trim().StartsWith("["))
        {
            string newJson = "{ \"Items\": " + json + "}";
            Wrapper<T> wrapper = JsonUtility.FromJson<Wrapper<T>>(newJson);
            return wrapper.Items;
        }
        
        // Se já vier formatado ou for outro caso, tenta direto
        try 
        {
            Wrapper<T> wrapper = JsonUtility.FromJson<Wrapper<T>>(json);
            return wrapper.Items;
        }
        catch
        {
            return new T[0];
        }
    }

    // Converte array em JSON
    public static string ToJson<T>(T[] array, bool prettyPrint = false)
    {
        Wrapper<T> wrapper = new Wrapper<T> { Items = array };
        return JsonUtility.ToJson(wrapper, prettyPrint);
    }

    private static string FixJson(string value)
    {
        // Se já estiver no formato { "Items": [...] }, não faz nada
        if (value.TrimStart().StartsWith("{"))
            return value;

        return "{\"Items\":" + value + "}";
    }

    [Serializable]
    private class Wrapper<T>
    {
        public T[] Items;
    }
}
