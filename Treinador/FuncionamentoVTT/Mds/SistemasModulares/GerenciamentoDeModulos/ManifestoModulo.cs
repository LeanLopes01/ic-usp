using System;

/// <summary>Espelha manifest.json de um módulo/Data Pack (ver dicevtt-gerenciamento-modulos.md §1).
/// Formato fixo e conhecido (não é JSON arbitrário) — por isso [Serializable] simples pra
/// JsonUtility.FromJson direto, diferente do que exige MiniJsonParser em CampanhaNpcImporter pra
/// JSON de estrutura desconhecida.</summary>
[Serializable]
public class ManifestoModulo
{
    public string id;
    public string name;
    public string version;
    public string author;
    public string[] dependencies = Array.Empty<string>();
    public string[] supported_languages = Array.Empty<string>();
    public string entry_file;
}
