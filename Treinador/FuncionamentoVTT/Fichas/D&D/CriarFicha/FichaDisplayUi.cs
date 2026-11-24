using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using UnityEngine;
using TMPro;

public class FichaDisplayUI : MonoBehaviour
{
    [Header("UI - Barra Lateral (Texto Único)")]
    public TMP_Text resumoLateralText;

    [Header("Resumo Expandido")]
    public bool mostrarResumoExpandido = true;

    [Serializable] public class FichaResponse { public string status; public FichaData ficha; }
    [Serializable] public class FichaData
    {
        public string name_pc;
        public int level_pc;
        public string vida;
        public string nome_raca;
        public string nome_classe;
        public string nome_antecedente;
        public string detalhes_principais;
    }
    [Serializable] public class DetalhesPrincipais { public SubData subrace; public SubData subclass; }
    [Serializable] public class SubData { public string name; }

    [Serializable]
    public class AtributosResponse
    {
        public string status;
        public AtributosData atributos = new AtributosData();
    }

    [Serializable]
    public class BonusBreakdownData
    {
        public string breakdown_forca;
        public string breakdown_destreza;
        public string breakdown_constituicao;
        public string breakdown_inteligencia;
        public string breakdown_sabedoria;
        public string breakdown_carisma;
    }

    [Serializable] private class ClasseInfoJsonWrapper { public ClasseInfoData classe_info; }
    [Serializable] private class ProficienciesInfoContainer { public ProficienciesInfoData proficiencies_info; }
    [Serializable] private class BackgroundInfoContainer { public BackgroundInfoData background_info; }
    // InventoryWrapper/InventorySaveItem e GrimorioWrapper/GrimorioSaveItem agora são as classes
    // canônicas e públicas de LocalFichaStorage.cs — não são mais declaradas aqui.
    [Serializable] private class RacaInfoSnapshot { public string subrace_id; public string subrace_name; public List<string> skills; public List<string> armors; public List<string> weapons; public List<string> tools; public List<string> languages; }
    [Serializable] private class ClasseInfoData { public string subclass_name; public List<string> saves; public List<string> skills; public List<string> armors; public List<string> weapons; public List<string> tools; public List<string> languages; public List<ClassPowerData> class_powers; public List<ClassPowerChoiceData> class_power_choices; public List<ClassPowerData> subclass_powers; }
    [Serializable] private class ClassPowerData { public string name; public int level; public string description; }
    [Serializable] private class ClassPowerChoiceData { public string feature_name; public int level; public string selected_option; }
    [Serializable] private class ProficienciesInfoData { public List<SkillSaveItem> skills; public List<SkillSaveItem> saves; public List<string> armors; public List<string> weapons; public List<string> tools; public List<string> languages; }
    [Serializable] private class SkillSaveItem { public string nome; public bool treinado; public int attrIndex; public int miscBonus; public int valorTotal; }
    [Serializable] private class BackgroundInfoData { public string nome; public string trait; public string ideal; public string bond; public string flaw; }

    private int _ultimaFichaIdCarregada = -1;

    private string nomeAtual = "Novo Personagem";
    private int nivelAtual = 1;
    private string vidaAtual = "0";
    private string racaAtual = "Raça";
    private string subracaAtual = string.Empty;
    private string classeAtual = "Classe";
    private string subclasseAtual = string.Empty;
    private string antecedenteAtual = "Antecedente";

    private AtributosResponse atributosAtuais = new AtributosResponse();
    private BonusBreakdownData breakdownAtual = new BonusBreakdownData();

    private List<string> salvaguardasTreinadas = new List<string>();
    private List<string> periciasTreinadas = new List<string>();
    private List<string> proficienciasArmas = new List<string>();
    private List<string> proficienciasArmaduras = new List<string>();
    private List<string> proficienciasFerramentas = new List<string>();
    private List<string> idiomasConhecidos = new List<string>();
    private List<string> poderesClasse = new List<string>();
    private List<string> poderesSubclasse = new List<string>();
    private List<string> escolhasClasse = new List<string>();

    private string antecedenteTracos = string.Empty;
    private string antecedenteIdeais = string.Empty;
    private string antecedenteVinculos = string.Empty;
    private string antecedenteFalhas = string.Empty;
    private float ouroAtual = 0f;
    private float pesoTotalAtual = 0f;
    private int itensSintonizados = 0;
    private int magiasPreparadas = 0;
    private int magiasNoGrimorio = 0;
    private string slotsMagiaResumo = "Nenhum";
    private string slotsMagiaDetalhes = "Sem detalhes.";

    private Dictionary<int, float> pesoPorItemId = new Dictionary<int, float>();
    private bool pesosCatalogoCarregados = false;
    private const string pastaItensLocalRelativa = "sistemas/quinta-edicao-srd/itens";

    private void OnEnable()
    {
        if (_ultimaFichaIdCarregada != GlobalFichaData.fichasDndId)
        {
            Debug.Log($"[FichaDisplayUI] OnEnable — ficha mudou ({_ultimaFichaIdCarregada} → {GlobalFichaData.fichasDndId}). Resetando display.");
            _ultimaFichaIdCarregada = GlobalFichaData.fichasDndId;
            atributosAtuais = new AtributosResponse();
            breakdownAtual  = new BonusBreakdownData();
        }
        AtualizarTudo();
    }

    public void AtualizarTudo()
    {
        CarregarDadosResumo();
        RenderizarResumoLateral();
    }

    public void AtualizarVidaLocal()
    {
        if (!string.IsNullOrEmpty(GlobalFichaData.vidaPersonagem))
        {
            vidaAtual = GlobalFichaData.vidaPersonagem;
        }

        RenderizarResumoLateral();
    }

    public void AtualizarDadosCompletos(AtributosResponse totais, BonusBreakdownData breakdown)
    {
        // Recarrega listas (perícias, salvaguardas, proficiências) do snapshot antes de renderizar,
        // garantindo que estejam sempre atualizadas. Em seguida, sobrescreve os atributos com os
        // valores vindos do AttributeManager, que são mais frescos do que os do snapshot.
        CarregarDadosResumo();
        if (totais != null) atributosAtuais = totais;
        if (breakdown != null) breakdownAtual = breakdown;
        RenderizarResumoLateral();
    }

    public void AtualizarAtributosDireto(AtributosResponse attr)
    {
        if (attr == null) return;
        atributosAtuais = attr;
        RenderizarResumoLateral();
    }

    private void CarregarDadosResumo()
    {
        nomeAtual = string.IsNullOrEmpty(GlobalFichaData.nomePersonagem) ? "Nome Desconhecido" : GlobalFichaData.nomePersonagem;
        nivelAtual = GlobalFichaData.levelPersonagem > 0 ? GlobalFichaData.levelPersonagem : 1;
        vidaAtual = string.IsNullOrEmpty(GlobalFichaData.vidaPersonagem) ? "0" : GlobalFichaData.vidaPersonagem;
        racaAtual = string.IsNullOrEmpty(GlobalFichaData.racaPersonagem) ? "Raça" : GlobalFichaData.racaPersonagem;
        classeAtual = string.IsNullOrEmpty(GlobalFichaData.classePersonagem) ? "Classe" : GlobalFichaData.classePersonagem;
        antecedenteAtual = GlobalFichaData.currentAntecedenteId > 0 ? $"Antecedente ID {GlobalFichaData.currentAntecedenteId}" : "Antecedente";

        LimparListasResumo();

        int playerId = DataManager.Instance != null ? DataManager.Instance.GetCurrentId() : 0;
        string steamId = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";

        if (LocalFichaStorage.TryGetFichaSnapshot(steamId, playerId, GlobalFichaData.fichasGeneralId, GlobalFichaData.fichasDndId, out LocalFichaData snapshot))
        {
            nomeAtual = string.IsNullOrWhiteSpace(snapshot.name_pc) ? nomeAtual : snapshot.name_pc;
            nivelAtual = snapshot.level_pc > 0 ? snapshot.level_pc : nivelAtual;
            if (snapshot.vida_atual > 0)
            {
                vidaAtual = snapshot.vida_atual.ToString();
            }
            else if (snapshot.vida_maxima > 0)
            {
                vidaAtual = snapshot.vida_maxima.ToString();
            }

            if (snapshot.raca != null && !string.IsNullOrWhiteSpace(snapshot.raca.raca_nome))
                racaAtual = snapshot.raca.raca_nome;
            var primaryCls = snapshot.classes?.PrimaryClass();
            if (primaryCls != null && !string.IsNullOrWhiteSpace(primaryCls.class_name))
                classeAtual = !string.IsNullOrWhiteSpace(snapshot.level_pc_detalhe)
                    ? snapshot.level_pc_detalhe : primaryCls.class_name;

            CarregarAtributosDoSnapshot(snapshot);
            CarregarResumoRaca(snapshot);
            CarregarResumoClasse(snapshot);
            CarregarResumoAntecedente(snapshot);
            CarregarResumoProficiencias(snapshot);
            CarregarResumoInventarioEMagias(snapshot);
        }
        else
        {
            CarregarFallbackEmMemoria();
            CarregarResumoInventarioEMagiasMemoria();
        }
    }

    private void LimparListasResumo()
    {
        salvaguardasTreinadas.Clear();
        periciasTreinadas.Clear();
        proficienciasArmas.Clear();
        proficienciasArmaduras.Clear();
        proficienciasFerramentas.Clear();
        idiomasConhecidos.Clear();
        poderesClasse.Clear();
        poderesSubclasse.Clear();
        escolhasClasse.Clear();
        antecedenteTracos = string.Empty;
        antecedenteIdeais = string.Empty;
        antecedenteVinculos = string.Empty;
        antecedenteFalhas = string.Empty;
        subracaAtual = string.Empty;
        subclasseAtual = string.Empty;
        ouroAtual = 0f;
        pesoTotalAtual = 0f;
        itensSintonizados = 0;
        magiasPreparadas = 0;
        magiasNoGrimorio = 0;
        slotsMagiaResumo = "Nenhum";
        slotsMagiaDetalhes = "Sem detalhes.";
    }

    private void CarregarFallbackEmMemoria()
    {
        if (!string.IsNullOrWhiteSpace(GlobalFichaData.detalhesPrincipaisJson))
        {
            try
            {
                DetalhesPrincipais detalhes = JsonUtility.FromJson<DetalhesPrincipais>(GlobalFichaData.detalhesPrincipaisJson);
                if (detalhes != null && detalhes.subrace != null && !string.IsNullOrWhiteSpace(detalhes.subrace.name))
                {
                    subracaAtual = detalhes.subrace.name.Trim();
                }

                if (detalhes != null && detalhes.subclass != null && !string.IsNullOrWhiteSpace(detalhes.subclass.name))
                {
                    subclasseAtual = detalhes.subclass.name.Trim();
                }
            }
            catch { }
        }

        // Sem snapshot local, mantemos somente dados básicos em memória.
    }

    private void CarregarAtributosDoSnapshot(LocalFichaData snapshot)
    {
        if (snapshot == null) return;

        atributosAtuais.atributos.forca = snapshot.atributos.forca;
        atributosAtuais.atributos.destreza = snapshot.atributos.destreza;
        atributosAtuais.atributos.constituicao = snapshot.atributos.constituicao;
        atributosAtuais.atributos.inteligencia = snapshot.atributos.inteligencia;
        atributosAtuais.atributos.sabedoria = snapshot.atributos.sabedoria;
        atributosAtuais.atributos.carisma = snapshot.atributos.carisma;
    }

    private void CarregarResumoRaca(LocalFichaData snapshot)
    {
        if (snapshot?.raca == null || string.IsNullOrEmpty(snapshot.raca.raca_id)) return;

        var raca = snapshot.raca;
        if (!string.IsNullOrWhiteSpace(raca.subraca_nome))
            subracaAtual = raca.subraca_nome.Trim();

        AdicionarListaUnica(periciasTreinadas, raca.skills);
        AdicionarListaUnica(proficienciasArmaduras, raca.armors);
        AdicionarListaUnica(proficienciasArmas, raca.weapons);
        AdicionarListaUnica(proficienciasFerramentas, raca.tools);
        AdicionarListaUnica(idiomasConhecidos, raca.languages);
    }

    private void CarregarResumoClasse(LocalFichaData snapshot)
    {
        var primary = snapshot?.classes?.PrimaryClass();
        if (primary == null) return;

        if (!string.IsNullOrWhiteSpace(primary.subclass_name))
            subclasseAtual = primary.subclass_name.Trim();

        AdicionarListaUnica(salvaguardasTreinadas, primary.saves);
        AdicionarListaUnica(periciasTreinadas, primary.skills);
        AdicionarListaUnica(proficienciasArmaduras, primary.armors);
        AdicionarListaUnica(proficienciasArmas, primary.weapons);
        AdicionarListaUnica(proficienciasFerramentas, primary.tools);
        AdicionarListaUnica(idiomasConhecidos, primary.languages);

        foreach (var poder in primary.class_powers ?? new System.Collections.Generic.List<ClassPowerEntry>())
        {
            if (poder == null || string.IsNullOrWhiteSpace(poder.name)) continue;
            string linha = poder.level > 0 ? $"Nível {poder.level}: {poder.name}" : poder.name;
            if (!string.IsNullOrWhiteSpace(poder.description)) linha += $" - {poder.description}";
            poderesClasse.Add(linha);
        }

        foreach (var poder in primary.subclass_powers ?? new System.Collections.Generic.List<ClassPowerEntry>())
        {
            if (poder == null || string.IsNullOrWhiteSpace(poder.name)) continue;
            string linha = poder.level > 0 ? $"Nível {poder.level}: {poder.name}" : poder.name;
            if (!string.IsNullOrWhiteSpace(poder.description)) linha += $" - {poder.description}";
            poderesSubclasse.Add(linha);
        }

        foreach (var escolha in primary.class_power_choices ?? new System.Collections.Generic.List<ClassPowerChoiceEntry>())
        {
            if (escolha == null) continue;
            string nome = !string.IsNullOrWhiteSpace(escolha.feature_name) ? escolha.feature_name : "Escolha de Classe";
            string valor = !string.IsNullOrWhiteSpace(escolha.selected_option) ? escolha.selected_option : "Sem opção registrada";
            escolhasClasse.Add(escolha.level > 0 ? $"Nível {escolha.level}: {nome} -> {valor}" : $"{nome} -> {valor}");
        }
    }

    private void CarregarResumoAntecedente(LocalFichaData snapshot)
    {
        if (snapshot == null) return;

        if (!string.IsNullOrWhiteSpace(snapshot.background_id))
            antecedenteAtual = $"Antecedente ID {snapshot.background_id}";

        if (string.IsNullOrWhiteSpace(snapshot.background_info_json)) return;

        try
        {
            BackgroundInfoContainer bgWrapper = JsonUtility.FromJson<BackgroundInfoContainer>(snapshot.background_info_json);
            BackgroundInfoData bg = bgWrapper != null ? bgWrapper.background_info : null;
            if (bg == null) return;

            if (!string.IsNullOrWhiteSpace(bg.nome))
                antecedenteAtual = bg.nome;

            antecedenteTracos   = bg.trait ?? string.Empty;
            antecedenteIdeais   = bg.ideal ?? string.Empty;
            antecedenteVinculos = bg.bond  ?? string.Empty;
            antecedenteFalhas   = bg.flaw  ?? string.Empty;
        }
        catch { }
    }

    private void CarregarResumoProficiencias(LocalFichaData snapshot)
    {
        if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.proficiencies_info_json)) return;

        try
        {
            ProficienciesInfoContainer profWrapper = JsonUtility.FromJson<ProficienciesInfoContainer>(snapshot.proficiencies_info_json);
            ProficienciesInfoData profInfo = profWrapper != null ? profWrapper.proficiencies_info : null;
            if (profInfo == null) return;

            if (profInfo.saves != null)
            {
                foreach (SkillSaveItem save in profInfo.saves)
                {
                    if (save != null && save.treinado && !string.IsNullOrWhiteSpace(save.nome))
                    {
                        salvaguardasTreinadas.Add(save.nome.Trim());
                    }
                }
            }

            if (profInfo.skills != null)
            {
                foreach (SkillSaveItem skill in profInfo.skills)
                {
                    if (skill != null && skill.treinado && !string.IsNullOrWhiteSpace(skill.nome))
                    {
                        periciasTreinadas.Add(skill.nome.Trim());
                    }
                }
            }

            AdicionarListaUnica(proficienciasArmaduras, profInfo.armors);
            AdicionarListaUnica(proficienciasArmas, profInfo.weapons);
            AdicionarListaUnica(proficienciasFerramentas, profInfo.tools);
            AdicionarListaUnica(idiomasConhecidos, profInfo.languages);
        }
        catch { }
    }

    private void AdicionarListaUnica(List<string> destino, IEnumerable<string> origem)
    {
        if (destino == null || origem == null) return;

        foreach (string item in origem)
        {
            if (string.IsNullOrWhiteSpace(item)) continue;
            string valor = item.Trim();
            if (!destino.Any(x => string.Equals(x, valor, StringComparison.OrdinalIgnoreCase)))
            {
                destino.Add(valor);
            }
        }
    }

    private void CarregarResumoInventarioEMagias(LocalFichaData snapshot)
    {
        if (snapshot == null) return;

        GarantirCatalogoPesosCarregado();

        InventoryWrapper inv = snapshot.inventario_json;
        if (inv != null)
        {
            ouroAtual = inv.gp;
            if (inv.itens != null)
            {
                itensSintonizados = inv.itens.Count(i => i != null && !string.IsNullOrWhiteSpace(i.equipSlot) && i.equipSlot.Equals("Sintonizado", StringComparison.OrdinalIgnoreCase));
                pesoTotalAtual = CalcularPesoInventario(inv.itens);
            }
        }

        GrimorioWrapper grimorio = snapshot.magias_json;
        if (grimorio != null && grimorio.magias != null)
        {
            magiasNoGrimorio = grimorio.magias.Count;
            magiasPreparadas = grimorio.magias.Count(m => m != null && m.preparada);
        }

        slotsMagiaResumo = ParseSlotsResumo(ObterJsonSlotsMagia(snapshot), out slotsMagiaDetalhes);
    }

    private void CarregarResumoInventarioEMagiasMemoria()
    {
        GarantirCatalogoPesosCarregado();

        InventoryWrapper inv = GlobalFichaData.inventarioJson;
        if (inv != null)
        {
            ouroAtual = inv.gp;
            if (inv.itens != null)
            {
                itensSintonizados = inv.itens.Count(i => i != null && !string.IsNullOrWhiteSpace(i.equipSlot) && i.equipSlot.Equals("Sintonizado", StringComparison.OrdinalIgnoreCase));
                pesoTotalAtual = CalcularPesoInventario(inv.itens);
            }
        }

        GrimorioWrapper grimorio = GlobalFichaData.magiasJson;
        if (grimorio != null && grimorio.magias != null)
        {
            magiasNoGrimorio = grimorio.magias.Count;
            magiasPreparadas = grimorio.magias.Count(m => m != null && m.preparada);
        }

        slotsMagiaResumo = ParseSlotsResumo(GlobalFichaData.slotsMagiasJson, out slotsMagiaDetalhes);
    }

    private string ParseSlotsResumo(string slotsJson, out string detalhes)
    {
        detalhes = "Sem dados de slot.";
        Dictionary<int, int> slotsPorNivel = ExtrairSlotsPorNivel(slotsJson);
        Debug.Log($"[FichaDisplayUI] ParseSlotsResumo. slotsJson='{slotsJson}' slotsCount={slotsPorNivel.Count}");
        if (slotsPorNivel.Count == 0)
        {
            return "Nenhum";
        }

        int totalSlots = slotsPorNivel.Values.Sum();
        int maiorNivel = slotsPorNivel.Keys.Max();
        detalhes = $"Total de slots: {totalSlots} | Maior nível com slot: {maiorNivel}";
        return string.Join(" | ", slotsPorNivel.OrderBy(s => s.Key).Select(s => $"Nv {s.Key}: {s.Value}"));
    }

    private string ObterJsonSlotsMagia(LocalFichaData snapshot)
    {
        if (snapshot != null && JsonSlotsTemDados(snapshot.slots_magias_json))
        {
            return snapshot.slots_magias_json;
        }

        if (JsonSlotsTemDados(GlobalFichaData.slotsMagiasJson))
        {
            return GlobalFichaData.slotsMagiasJson;
        }

        if (snapshot != null && !string.IsNullOrWhiteSpace(snapshot.slots_magias_json))
        {
            return snapshot.slots_magias_json;
        }

        return GlobalFichaData.slotsMagiasJson ?? string.Empty;
    }

    private static bool JsonSlotsTemDados(string slotsJson)
    {
        if (string.IsNullOrWhiteSpace(slotsJson)) return false;
        return Regex.IsMatch(slotsJson, "\\\"(?<lvl>\\d+)\\\"\\s*:\\s*(?<qtd>\\d+)");
    }

    private Dictionary<int, int> ExtrairSlotsPorNivel(string slotsJson)
    {
        Dictionary<int, int> slots = new Dictionary<int, int>();
        if (string.IsNullOrWhiteSpace(slotsJson))
        {
            Debug.Log("[FichaDisplayUI] ExtrairSlotsPorNivel: slotsJson vazio.");
            return slots;
        }

        MatchCollection matches = Regex.Matches(slotsJson, "\\\"(?<lvl>\\d+)\\\"\\s*:\\s*(?<qtd>\\d+)");
        foreach (Match match in matches)
        {
            if (!match.Success) continue;
            if (int.TryParse(match.Groups["lvl"].Value, out int nivel) && int.TryParse(match.Groups["qtd"].Value, out int quantidade))
            {
                slots[nivel] = quantidade;
            }
        }

        Debug.Log($"[FichaDisplayUI] ExtrairSlotsPorNivel -> '{slotsJson}' => {string.Join(", ", slots.OrderBy(x => x.Key).Select(x => $"Nv{x.Key}={x.Value}"))}");

        return slots;
    }

    private void GarantirCatalogoPesosCarregado()
    {
        if (pesosCatalogoCarregados) return;
        pesosCatalogoCarregados = true;
        pesoPorItemId.Clear();

        try
        {
            string pasta = DndSrdLocalPath.Resolver(pastaItensLocalRelativa);
            if (!Directory.Exists(pasta)) return;

            foreach (string arquivo in Directory.GetFiles(pasta, "*.xml", SearchOption.AllDirectories))
            {
                try
                {
                    string xml = File.ReadAllText(arquivo, Encoding.UTF8).Trim().Replace("\uFEFF", "");
                    XDocument doc = XDocument.Parse(xml);
                    XElement root = doc.Element("item") ?? doc.Root;
                    if (root == null) continue;

                    string nome = root.Element("name")?.Value?.Trim();
                    if (string.IsNullOrWhiteSpace(nome)) continue;

                    string idLogico = root.Attribute("id")?.Value;
                    int id = GerarIdDeterministico(idLogico, nome);

                    string weightStr = root.Element("metadata")?.Element("weight")?.Value;
                    float peso = ConverterPesoParaFloat(weightStr);
                    pesoPorItemId[id] = peso;
                }
                catch { }
            }
        }
        catch { }
    }

    private float CalcularPesoInventario(List<InventorySaveItem> itens)
    {
        if (itens == null) return 0f;

        float total = 0f;
        foreach (InventorySaveItem item in itens)
        {
            if (item == null) continue;
            if (item.id <= 0) continue;
            if (pesoPorItemId.TryGetValue(item.id, out float peso))
            {
                total += peso;
            }
        }

        return total;
    }

    private static float ConverterPesoParaFloat(string weightString)
    {
        if (string.IsNullOrWhiteSpace(weightString) || weightString == "-") return 0f;
        string num = new string(weightString.Where(c => char.IsDigit(c) || c == '.' || c == ',').ToArray()).Replace(',', '.');
        if (float.TryParse(num, NumberStyles.Any, CultureInfo.InvariantCulture, out float res)) return res;
        return 0f;
    }

    private static int GerarIdDeterministico(string idLogico, string nome)
    {
        string chave = (idLogico ?? "") + "|" + (nome ?? "");
        unchecked
        {
            int hash = 23;
            for (int i = 0; i < chave.Length; i++) hash = (hash * 31) + chave[i];
            if (hash == int.MinValue) hash = int.MaxValue;
            hash = Mathf.Abs(hash);
            return hash == 0 ? 1 : hash;
        }
    }

    private string FormatarAtributo(string sigla, int valorFinal, string expressao)
    {
        string sinal = valorFinal >= 0 ? "+" : "";
        return $"{sigla}: {sinal}{valorFinal} ({expressao})";
    }

    private string ObterExpressaoBreakdown(string expressao, int valorFinal)
    {
        return string.IsNullOrWhiteSpace(expressao) ? valorFinal.ToString() : expressao;
    }

    private string FormatarLista(string titulo, IEnumerable<string> itens)
    {
        List<string> lista = itens != null
            ? itens.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : new List<string>();

        if (lista.Count == 0)
        {
            return $"<b>{titulo}:</b> Nenhuma";
        }

        return $"<b>{titulo}:</b> {string.Join(", ", lista)}";
    }

    private void RenderizarResumoLateral()
    {
        if (resumoLateralText == null) return;

        string forBreak = ObterExpressaoBreakdown(breakdownAtual?.breakdown_forca, atributosAtuais.atributos.forca);
        string dexBreak = ObterExpressaoBreakdown(breakdownAtual?.breakdown_destreza, atributosAtuais.atributos.destreza);
        string conBreak = ObterExpressaoBreakdown(breakdownAtual?.breakdown_constituicao, atributosAtuais.atributos.constituicao);
        string intBreak = ObterExpressaoBreakdown(breakdownAtual?.breakdown_inteligencia, atributosAtuais.atributos.inteligencia);
        string sabBreak = ObterExpressaoBreakdown(breakdownAtual?.breakdown_sabedoria, atributosAtuais.atributos.sabedoria);
        string carBreak = ObterExpressaoBreakdown(breakdownAtual?.breakdown_carisma, atributosAtuais.atributos.carisma);

        string forLabel = FormatarAtributo("FOR", atributosAtuais.atributos.forca, forBreak);
        string dexLabel = FormatarAtributo("DEX", atributosAtuais.atributos.destreza, dexBreak);
        string conLabel = FormatarAtributo("CON", atributosAtuais.atributos.constituicao, conBreak);
        string intLabel = FormatarAtributo("INT", atributosAtuais.atributos.inteligencia, intBreak);
        string sabLabel = FormatarAtributo("SAB", atributosAtuais.atributos.sabedoria, sabBreak);
        string carLabel = FormatarAtributo("CAR", atributosAtuais.atributos.carisma, carBreak);

        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"<b>{nomeAtual}</b>");
        sb.AppendLine($"Nível: {nivelAtual}");
        sb.AppendLine($"Vida: {vidaAtual}");
        sb.AppendLine($"Raça: {racaAtual}{(string.IsNullOrWhiteSpace(subracaAtual) ? string.Empty : $" / {subracaAtual}")}");
        sb.AppendLine($"Classe: {classeAtual}{(string.IsNullOrWhiteSpace(subclasseAtual) ? string.Empty : $" / {subclasseAtual}")}");
        sb.AppendLine($"Origem: {antecedenteAtual}");
        sb.AppendLine($"<b>Slots de Magia:</b> {slotsMagiaResumo}");
        sb.AppendLine($"<b>Detalhes dos Slots:</b> {slotsMagiaDetalhes}");

        if (mostrarResumoExpandido)
        {
            sb.AppendLine();
            sb.AppendLine("<b>Atributos (Modificadores)</b>");
            sb.AppendLine(forLabel);
            sb.AppendLine(dexLabel);
            sb.AppendLine(conLabel);
            sb.AppendLine(intLabel);
            sb.AppendLine(sabLabel);
            sb.AppendLine(carLabel);

            sb.AppendLine();
            sb.AppendLine("<b>Salvaguardas</b>");
            sb.AppendLine(FormatarLista("Treinadas", salvaguardasTreinadas));

            sb.AppendLine();
            sb.AppendLine("<b>Perícias</b>");
            sb.AppendLine(FormatarLista("Treinadas", periciasTreinadas));

            sb.AppendLine();
            sb.AppendLine("<b>Proficiências</b>");
            sb.AppendLine(FormatarLista("Armas", proficienciasArmas));
            sb.AppendLine(FormatarLista("Armaduras", proficienciasArmaduras));
            sb.AppendLine(FormatarLista("Ferramentas", proficienciasFerramentas));
            sb.AppendLine(FormatarLista("Idiomas", idiomasConhecidos));

            sb.AppendLine();
            sb.AppendLine("<b>Recursos</b>");
            sb.AppendLine($"<b>Ouro:</b> {ouroAtual:0.##} GP");
            sb.AppendLine($"<b>Peso Total:</b> {pesoTotalAtual:0.##} lb");
            sb.AppendLine($"<b>Itens Sintonizados:</b> {itensSintonizados}/3");
            sb.AppendLine($"<b>Grimório:</b> {magiasNoGrimorio} magias ({magiasPreparadas} preparadas)");

            if (poderesClasse.Count > 0 || poderesSubclasse.Count > 0 || escolhasClasse.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("<b>Habilidades de Classe</b>");

                if (poderesClasse.Count > 0)
                {
                    sb.AppendLine("<b>Poderes da Classe:</b>");
                    foreach (string item in poderesClasse) sb.AppendLine($"- {item}");
                }

                if (poderesSubclasse.Count > 0)
                {
                    sb.AppendLine("<b>Poderes da Subclasse:</b>");
                    foreach (string item in poderesSubclasse) sb.AppendLine($"- {item}");
                }

                if (escolhasClasse.Count > 0)
                {
                    sb.AppendLine("<b>Escolhas da Classe:</b>");
                    foreach (string item in escolhasClasse) sb.AppendLine($"- {item}");
                }
            }

            if (!string.IsNullOrWhiteSpace(antecedenteTracos) || !string.IsNullOrWhiteSpace(antecedenteIdeais) || !string.IsNullOrWhiteSpace(antecedenteVinculos) || !string.IsNullOrWhiteSpace(antecedenteFalhas))
            {
                sb.AppendLine();
                sb.AppendLine("<b>Antecedente</b>");
                if (!string.IsNullOrWhiteSpace(antecedenteTracos)) sb.AppendLine($"<b>Traço:</b> {antecedenteTracos}");
                if (!string.IsNullOrWhiteSpace(antecedenteIdeais)) sb.AppendLine($"<b>Ideal:</b> {antecedenteIdeais}");
                if (!string.IsNullOrWhiteSpace(antecedenteVinculos)) sb.AppendLine($"<b>Vínculo:</b> {antecedenteVinculos}");
                if (!string.IsNullOrWhiteSpace(antecedenteFalhas)) sb.AppendLine($"<b>Falha:</b> {antecedenteFalhas}");
            }
        }

        resumoLateralText.text = sb.ToString().TrimEnd();
    }
}