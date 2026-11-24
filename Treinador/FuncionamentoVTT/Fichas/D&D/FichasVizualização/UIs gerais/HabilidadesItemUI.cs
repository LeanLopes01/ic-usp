using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class HabilidadesItemUI : MonoBehaviour
{
    [Header("Resumo (Cabeçalho)")]
    public TMP_InputField nomeInput;
    public TMP_InputField acaoInput;   // Tipo de ação: Ação, Ação Bônus, Reação, Passiva
    public TMP_InputField grupoInput;  // Categoria temática do efeito (Combate, Utilidade, Social, Exploração)
    public TMP_InputField fonteInput;  // Classe, Subclasse, Raça, Sub-raça, Talento, etc.
    public TMP_InputField tipoInput;   // Resumo de 1 palavra do que a habilidade faz (Dano, Cura, Buff, Debuff, etc.)
    public TMP_InputField usosInput;   // Ilimitado, Descanso Curto, Descanso Longo

    [Header("Controles")]
    public Button btnExpandirDetalhes;
    public Button btnColapsarDetalhes;
    public GameObject conteudoDetalhes;

    [Header("Conteúdo Expansível - Detalhes")]
    public TMP_InputField nivelDetalheInput;
    public TMP_InputField acaoDetalheInput;
    public TMP_InputField grupoDetalheInput;
    public TMP_InputField fonteDetalheInput;
    public TMP_InputField tipoDetalheInput;
    public TMP_InputField usosDetalheInput;
    public TMP_InputField descricaoCompletaInput;

    // Identidade estrutural da habilidade (nome/categoria/nível/origem calculados na fonte),
    // usada pelo manager para casar edições com o override salvo — não é exibida na UI.
    private string chave;

    public void Setup(string chaveHabilidade, string nome, string acao, string grupo, string fonte, string tipo, string usos, string nivel, string descricaoCompleta)
    {
        chave = chaveHabilidade;

        if (nomeInput != null) nomeInput.SetTextWithoutNotify(nome);
        if (acaoInput != null) acaoInput.SetTextWithoutNotify(string.IsNullOrEmpty(acao) ? "Passiva" : acao);
        if (grupoInput != null) grupoInput.SetTextWithoutNotify(string.IsNullOrEmpty(grupo) ? "-" : grupo);
        if (fonteInput != null) fonteInput.SetTextWithoutNotify(string.IsNullOrEmpty(fonte) ? "-" : fonte);
        if (tipoInput != null) tipoInput.SetTextWithoutNotify(string.IsNullOrEmpty(tipo) ? "-" : tipo);
        if (usosInput != null) usosInput.SetTextWithoutNotify(string.IsNullOrEmpty(usos) ? "Ilimitado" : usos);

        if (nivelDetalheInput != null) nivelDetalheInput.SetTextWithoutNotify(string.IsNullOrEmpty(nivel) ? "" : nivel);
        if (acaoDetalheInput != null) acaoDetalheInput.SetTextWithoutNotify(string.IsNullOrEmpty(acao) ? "Passiva" : acao);
        if (grupoDetalheInput != null) grupoDetalheInput.SetTextWithoutNotify(string.IsNullOrEmpty(grupo) ? "-" : grupo);
        if (fonteDetalheInput != null) fonteDetalheInput.SetTextWithoutNotify(string.IsNullOrEmpty(fonte) ? "-" : fonte);
        if (tipoDetalheInput != null) tipoDetalheInput.SetTextWithoutNotify(string.IsNullOrEmpty(tipo) ? "-" : tipo);
        if (usosDetalheInput != null) usosDetalheInput.SetTextWithoutNotify(string.IsNullOrEmpty(usos) ? "Ilimitado" : usos);
        if (descricaoCompletaInput != null) descricaoCompletaInput.SetTextWithoutNotify(string.IsNullOrEmpty(descricaoCompleta) ? "Sem descrição." : descricaoCompleta);

        ConfigurarTruncamentoComReticencias(nomeInput);
        ConfigurarTruncamentoComReticencias(acaoInput);
        ConfigurarTruncamentoComReticencias(grupoInput);
        ConfigurarTruncamentoComReticencias(fonteInput);
        ConfigurarTruncamentoComReticencias(tipoInput);
        ConfigurarTruncamentoComReticencias(usosInput);
        ConfigurarTruncamentoComReticencias(nivelDetalheInput);
        ConfigurarTruncamentoComReticencias(acaoDetalheInput);
        ConfigurarTruncamentoComReticencias(grupoDetalheInput);
        ConfigurarTruncamentoComReticencias(fonteDetalheInput);
        ConfigurarTruncamentoComReticencias(tipoDetalheInput);
        ConfigurarTruncamentoComReticencias(usosDetalheInput);

        ConfigurarExpansaoDetalhes();
    }

    // Faz o texto interno do InputField cortar com "..." em vez de estourar o layout quando
    // não cabe na largura do campo (ex: "Canalizar Divindade: Afastar Mortos-Vivos" -> "Canalizar Divinda...").
    // Não é aplicado em descricaoCompletaInput, que deve quebrar linha normalmente.
    private void ConfigurarTruncamentoComReticencias(TMP_InputField campo)
    {
        if (campo == null || campo.textComponent == null) return;

        campo.textComponent.textWrappingMode = TextWrappingModes.NoWrap;
        campo.textComponent.overflowMode = TextOverflowModes.Ellipsis;
    }

    /// <summary>
    /// Liga o autosave genérico do cabeçalho e do detalhe (ação, grupo, fonte, tipo, usos, descrição completa).
    /// Cabeçalho e detalhe representam o mesmo dado — editar um reflete no outro só ao reabrir/recarregar.
    /// </summary>
    public void RegistrarAutoSave(Action onAlterado)
    {
        RegistrarEndEdit(nomeInput, onAlterado);
        RegistrarEndEdit(acaoInput, onAlterado);
        RegistrarEndEdit(grupoInput, onAlterado);
        RegistrarEndEdit(fonteInput, onAlterado);
        RegistrarEndEdit(tipoInput, onAlterado);
        RegistrarEndEdit(usosInput, onAlterado);
        RegistrarEndEdit(nivelDetalheInput, onAlterado);
        RegistrarEndEdit(acaoDetalheInput, onAlterado);
        RegistrarEndEdit(grupoDetalheInput, onAlterado);
        RegistrarEndEdit(fonteDetalheInput, onAlterado);
        RegistrarEndEdit(tipoDetalheInput, onAlterado);
        RegistrarEndEdit(usosDetalheInput, onAlterado);
        RegistrarEndEdit(descricaoCompletaInput, onAlterado);
    }

    private void RegistrarEndEdit(TMP_InputField campo, Action onAlterado)
    {
        if (campo == null) return;
        campo.onEndEdit.RemoveAllListeners();
        campo.onEndEdit.AddListener((_) => onAlterado?.Invoke());
    }

    /// <summary>
    /// Coleta os valores atuais. Prioriza o campo do detalhe expandido quando ele foi editado
    /// por último (o detalhe é o mais completo); cai no cabeçalho como padrão.
    /// </summary>
    public HabilidadeOverrideData ObterDadosAtuais()
    {
        return new HabilidadeOverrideData
        {
            chave = chave,
            nome = nomeInput ? nomeInput.text : "",
            acao = acaoDetalheInput ? acaoDetalheInput.text : (acaoInput ? acaoInput.text : ""),
            grupo = grupoDetalheInput ? grupoDetalheInput.text : (grupoInput ? grupoInput.text : ""),
            fonte = fonteDetalheInput ? fonteDetalheInput.text : (fonteInput ? fonteInput.text : ""),
            tipo = tipoDetalheInput ? tipoDetalheInput.text : (tipoInput ? tipoInput.text : ""),
            usos = usosDetalheInput ? usosDetalheInput.text : (usosInput ? usosInput.text : ""),
            descricao = descricaoCompletaInput ? descricaoCompletaInput.text : ""
        };
    }

    private void ConfigurarExpansaoDetalhes()
    {
        RectTransform conteudoRect = conteudoDetalhes != null ? conteudoDetalhes.GetComponent<RectTransform>() : null;
        RectTransform listaParaReconstruir = transform.parent as RectTransform;

        if (conteudoDetalhes != null) conteudoDetalhes.SetActive(false);
        if (btnColapsarDetalhes != null) btnColapsarDetalhes.gameObject.SetActive(false);
        if (btnExpandirDetalhes != null) btnExpandirDetalhes.gameObject.SetActive(true);

        if (btnExpandirDetalhes != null)
        {
            btnExpandirDetalhes.onClick.RemoveAllListeners();
            btnExpandirDetalhes.onClick.AddListener(() =>
            {
                btnExpandirDetalhes.gameObject.SetActive(false);
                if (btnColapsarDetalhes != null) btnColapsarDetalhes.gameObject.SetActive(true);
                UIAnimacao.AnimarExpansao(this, conteudoRect, true, listaParaReconstruir);
            });
        }

        if (btnColapsarDetalhes != null)
        {
            btnColapsarDetalhes.onClick.RemoveAllListeners();
            btnColapsarDetalhes.onClick.AddListener(() =>
            {
                btnColapsarDetalhes.gameObject.SetActive(false);
                if (btnExpandirDetalhes != null) btnExpandirDetalhes.gameObject.SetActive(true);
                UIAnimacao.AnimarExpansao(this, conteudoRect, false, listaParaReconstruir);
            });
        }
    }
}

// Override persistido de uma habilidade (edição manual do jogador por cima do valor calculado).
// "chave" é a identidade estrutural (nome/categoria/nível/origem calculados na fonte), não o nome editado.
[Serializable]
public class HabilidadeOverrideData
{
    public string chave;
    public string nome;
    public string acao;
    public string grupo;
    public string fonte;
    public string tipo;
    public string usos;
    public string descricao;
}
