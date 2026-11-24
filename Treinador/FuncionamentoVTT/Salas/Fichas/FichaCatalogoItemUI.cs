using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class FichaCatalogoItemUI : MonoBehaviour
{
    [Header("Cabeçalho")]
    [SerializeField] private Image   tokenImagem;
    [SerializeField] private TMP_Text textoNome;
    [SerializeField] private TMP_Text textoCabecalho;

    [Header("Dados")]
    [SerializeField] private Transform  dadosContent;
    [SerializeField] private GameObject prefabDadoItem;

    private Texture2D _textura;

    private void OnDestroy()
    {
        if (_textura != null) Destroy(_textura);
    }

    // â”€â”€ Setup â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    public void Setup(LocalFichaData snap)
    {
        if (textoNome      != null) textoNome.text      = string.IsNullOrWhiteSpace(snap.name_pc) ? "Ficha" : snap.name_pc;
        if (textoCabecalho != null) textoCabecalho.text = MontarCabecalho(snap);

        CarregarToken(snap.token_image_path);
        PopularDados(ExtrairDados(snap));
    }

    public void SetupFallback(FichaData ficha)
    {
        if (textoNome      != null) textoNome.text      = string.IsNullOrWhiteSpace(ficha.name_pc) ? "Ficha" : ficha.name_pc;
        if (textoCabecalho != null) textoCabecalho.text = $"Nível {ficha.level_pc} â€¢ {SafeText(ficha.race_pc)} â€¢ {SafeText(ficha.class_pc)}";

        PopularDados(new List<(string, string)>
        {
            ("Nível",  ficha.level_pc.ToString()),
            ("Raça",   SafeText(ficha.race_pc)),
            ("Classe", SafeText(ficha.class_pc))
        });
    }

    // â”€â”€ Dados â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private void PopularDados(List<(string titulo, string descricao)> lista)
    {
        if (dadosContent == null || prefabDadoItem == null) return;

        for (int i = dadosContent.childCount - 1; i >= 0; i--)
            Destroy(dadosContent.GetChild(i).gameObject);

        foreach ((string titulo, string descricao) in lista)
        {
            GameObject go   = UIUtils.Spawn(prefabDadoItem, dadosContent);
            var        item = go.GetComponent<FichaCatalogoDadoItemUI>();
            item?.Setup(titulo, descricao);
        }
    }

    private static List<(string, string)> ExtrairDados(LocalFichaData snap)
    {
        var dados = new List<(string, string)>();

        // â”€â”€ Identidade â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var primaryCls = snap.classes?.PrimaryClass();
        string clsDisplay = !string.IsNullOrWhiteSpace(snap.level_pc_detalhe)
            ? snap.level_pc_detalhe : primaryCls?.class_name ?? "";
        if (!IsVazio(clsDisplay)) dados.Add(("Classe", clsDisplay));
        if (snap.raca != null && !IsVazio(snap.raca.raca_nome)) dados.Add(("Raça", snap.raca.raca_nome));
        dados.Add(("Nível", snap.level_pc.ToString()));

        // â”€â”€ Classe detalhada (lê de snap.classes) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        if (primaryCls != null)
        {
            if (!IsVazio(primaryCls.subclass_name))
                dados.Add(("Subclasse", primaryCls.subclass_name));

            if (primaryCls.saves != null && primaryCls.saves.Count > 0)
                dados.Add(("Salvaguardas", string.Join(", ", primaryCls.saves)));

            if (primaryCls.class_powers != null && primaryCls.class_powers.Count > 0)
            {
                var nomes = new List<string>();
                foreach (var p in primaryCls.class_powers)
                    if (!IsVazio(p.name)) nomes.Add(p.name);
                if (nomes.Count > 0)
                    dados.Add(("Habilidades de Classe", string.Join("\n", nomes)));
            }

            if (primaryCls.subclass_powers != null && primaryCls.subclass_powers.Count > 0)
            {
                var nomes = new List<string>();
                foreach (var p in primaryCls.subclass_powers)
                    if (!IsVazio(p.name)) nomes.Add(p.name);
                if (nomes.Count > 0)
                    dados.Add(("Habilidades de Subclasse", string.Join("\n", nomes)));
            }

            if (primaryCls.asi_feats != null && primaryCls.asi_feats.Count > 0)
                dados.Add(("Talentos / ASI", string.Join("\n", primaryCls.asi_feats)));
        }

        // â”€â”€ Raça detalhada (lê de snap.raca) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        if (snap.raca != null && !IsVazio(snap.raca.subraca_nome))
            dados.Add(("Sub-raça", snap.raca.subraca_nome));

        // â”€â”€ Antecedente â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        if (!IsVazio(snap.background_info_json))
        {
            try
            {
                var bi = JsonUtility.FromJson<BackgroundInfoWrapper>(snap.background_info_json)?.background_info;
                if (bi != null)
                {
                    if (!IsVazio(bi.nome))  dados.Add(("Antecedente", bi.nome));
                    if (!IsVazio(bi.trait)) dados.Add(("Traço",       bi.trait));
                    if (!IsVazio(bi.ideal)) dados.Add(("Ideal",        bi.ideal));
                    if (!IsVazio(bi.bond))  dados.Add(("Vínculo",      bi.bond));
                    if (!IsVazio(bi.flaw))  dados.Add(("Defeito",      bi.flaw));
                }
            }
            catch { }
        }

        // â”€â”€ Combate â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        if (snap.vida_maxima > 0)
            dados.Add(("Pontos de Vida", $"{snap.vida_atual} / {snap.vida_maxima}"));
        if (snap.vida_temporaria > 0)
            dados.Add(("PV Temporários", snap.vida_temporaria.ToString()));
        if (!IsVazio(snap.dado_vida))
            dados.Add(("Dado de Vida", snap.dado_vida));

        // â”€â”€ Atributos â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        if (snap.atributos.forca + snap.atributos.destreza + snap.atributos.constituicao
          + snap.atributos.inteligencia + snap.atributos.sabedoria + snap.atributos.carisma > 0)
        {
            string atrs =
                $"FOR {snap.atributos.forca}   DEX {snap.atributos.destreza}   CON {snap.atributos.constituicao}\n" +
                $"INT {snap.atributos.inteligencia}   SAB {snap.atributos.sabedoria}   CAR {snap.atributos.carisma}";
            dados.Add(("Atributos", atrs));
        }

        // â”€â”€ Proficiências â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        if (!IsVazio(snap.proficiencies_info_json))
        {
            try
            {
                var prof = JsonUtility.FromJson<ProficienciasWrapper>(snap.proficiencies_info_json);
                if (prof != null)
                {
                    if (prof.skills != null && prof.skills.Count > 0)
                    {
                        var treinadas = new List<string>();
                        foreach (var s in prof.skills)
                            if (s.treinado && !IsVazio(s.nome)) treinadas.Add(s.nome);
                        if (treinadas.Count > 0)
                            dados.Add(("Perícias", string.Join(", ", treinadas)));
                    }
                    if (prof.languages != null && prof.languages.Count > 0)
                        dados.Add(("Idiomas", string.Join(", ", prof.languages)));
                    if (prof.tools != null && prof.tools.Count > 0)
                        dados.Add(("Ferramentas", string.Join(", ", prof.tools)));
                    if (prof.armors != null && prof.armors.Count > 0)
                        dados.Add(("Armaduras", string.Join(", ", prof.armors)));
                    if (prof.weapons != null && prof.weapons.Count > 0)
                        dados.Add(("Armas", string.Join(", ", prof.weapons)));
                }
            }
            catch { }
        }

        // â”€â”€ Magias â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var grimorio = snap.magias_json;
        if (grimorio?.magias != null && grimorio.magias.Count > 0)
            dados.Add(("Magias (total)", grimorio.magias.Count.ToString()));

        // â”€â”€ Inventário â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var inv = snap.inventario_json;
        if (inv != null)
        {
            if (inv.gp > 0)
                dados.Add(("Ouro", inv.gp.ToString("0.##") + " PO"));

            if (inv.itens != null && inv.itens.Count > 0)
            {
                var nomes = new List<string>();
                foreach (var item in inv.itens)
                {
                    string nome = IsVazio(item.customName) ? $"Item #{item.id}" : item.customName;
                    if (!IsVazio(item.equipSlot) && item.equipSlot != "Desequipado")
                        nome += $" ({item.equipSlot})";
                    nomes.Add(nome);
                }
                if (nomes.Count > 0)
                    dados.Add(("Inventário", string.Join("\n", nomes)));
            }
        }

        return dados;
    }

    // â”€â”€ Estruturas de parsing JSON â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    [Serializable] private class ClasseInfoWrapper  { public ClasseInfoData  classe_info; }
    [Serializable] private class BackgroundInfoWrapper { public BackgroundInfoData background_info; }

    [Serializable]
    private class ClasseInfoData
    {
        public string subclass_name;
        public List<string> saves       = new List<string>();
        public List<string> asi_feats   = new List<string>();
        public List<ClassPowerData> class_powers    = new List<ClassPowerData>();
        public List<ClassPowerData> subclass_powers = new List<ClassPowerData>();
    }

    [Serializable] private class ClassPowerData { public string name; public int level; }

    [Serializable]
    private class BackgroundInfoData
    {
        public string nome;
        public string trait;
        public string ideal;
        public string bond;
        public string flaw;
    }

    [Serializable]
    private class RacaInfoSnapshot
    {
        public string subrace_name;
    }

    [Serializable]
    private class ProficienciasWrapper
    {
        public List<SkillItem> skills    = new List<SkillItem>();
        public List<string>    languages = new List<string>();
        public List<string>    tools     = new List<string>();
        public List<string>    armors    = new List<string>();
        public List<string>    weapons   = new List<string>();
    }

    [Serializable] private class SkillItem { public string nome; public bool treinado; }

    // GrimorioWrapper/GrimorioSaveItem e InventoryWrapper/InventorySaveItem agora são as classes
    // canônicas e públicas de LocalFichaStorage.cs — não são mais declaradas aqui.

    // â”€â”€ Token â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private void CarregarToken(string caminho)
    {
        if (tokenImagem == null) return;

        tokenImagem.sprite = null;
        tokenImagem.enabled = false;

        if (string.IsNullOrWhiteSpace(caminho) || !File.Exists(caminho)) return;

        byte[] bytes = File.ReadAllBytes(caminho);
        _textura = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!_textura.LoadImage(bytes)) { Destroy(_textura); _textura = null; return; }

        tokenImagem.sprite  = Sprite.Create(_textura, new Rect(0, 0, _textura.width, _textura.height), new Vector2(0.5f, 0.5f), 100f);
        tokenImagem.enabled = true;
    }

    // â”€â”€ Helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static string MontarCabecalho(LocalFichaData snap)
    {
        string nivel  = $"Nível {snap.level_pc}";
        string raca   = snap.raca?.raca_nome?.Trim() ?? string.Empty;
        string classe = (!string.IsNullOrWhiteSpace(snap.level_pc_detalhe)
            ? snap.level_pc_detalhe
            : snap.classes?.PrimaryClass()?.class_name ?? "").Trim();

        if (!string.IsNullOrEmpty(raca) && !string.IsNullOrEmpty(classe))
            return $"{nivel} â€¢ {raca} â€¢ {classe}";
        if (!string.IsNullOrEmpty(classe))
            return $"{nivel} â€¢ {classe}";
        return nivel;
    }

    private static bool   IsVazio(string s)   => string.IsNullOrWhiteSpace(s) || s.Trim() == "-";
    private static string SafeText(string s)  => string.IsNullOrWhiteSpace(s) ? "-" : s.Trim();
}
