using System;

/// <summary>
/// Dados de configuração passados ao PopupUI pelo PopupManager.
/// Use os factory methods estáticos para construir configs prontas,
/// ou preencha os campos manualmente para casos personalizados.
/// </summary>
public class PopupConfig
{
    // ── Conteúdo ─────────────────────────────────────────────────
    public string Titulo    = "";
    public string Mensagem  = "";

    // ── Botões ───────────────────────────────────────────────────
    public bool   MostrarBotaoConfirmar = false;
    public bool   MostrarBotaoCancelar  = false;
    public string LabelConfirmar        = "OK";
    public string LabelCancelar         = "Cancelar";
    public Action OnConfirmar;
    public Action OnCancelar;

    // ── Dropdown de nível ────────────────────────────────────────
    public bool          MostrarDropdownNivel = false;
    public int           NivelMinDropdown     = 2;
    public Action<int>   OnNivelSelecionado;

    // ── Campo de texto (popup de edição) ────────────────────────
    public bool            MostrarInputTexto = false;
    public string          TextoInicial      = "";
    public string          PlaceholderTexto  = "";
    public Action<string>  OnTextoConfirmado;

    // ── Visuais ──────────────────────────────────────────────────
    public bool MostrarSpinner     = false;
    public bool MostrarIconeErro   = false;
    public bool MostrarIconeInfo   = false;
    public bool MostrarIconeSucesso = false;

    // ── Factory methods ──────────────────────────────────────────

    public static PopupConfig Carregamento(string titulo, string mensagem = "Aguarde...") =>
        new PopupConfig
        {
            Titulo         = titulo,
            Mensagem       = mensagem,
            MostrarSpinner = true,
        };

    public static PopupConfig Confirmacao(
        string titulo, string mensagem,
        Action onSim, Action onNao,
        string labelSim = "Sim", string labelNao = "Não") =>
        new PopupConfig
        {
            Titulo                = titulo,
            Mensagem              = mensagem,
            MostrarBotaoConfirmar = true,
            MostrarBotaoCancelar  = true,
            LabelConfirmar        = labelSim,
            LabelCancelar         = labelNao,
            OnConfirmar           = onSim,
            OnCancelar            = onNao,
            MostrarIconeInfo      = true,
        };

    public static PopupConfig Erro(string titulo, string mensagem, Action onOk = null) =>
        new PopupConfig
        {
            Titulo                = string.IsNullOrEmpty(titulo) ? "Erro" : titulo,
            Mensagem              = mensagem,
            MostrarBotaoConfirmar = true,
            LabelConfirmar        = "OK",
            OnConfirmar           = onOk,
            MostrarIconeErro      = true,
        };

    public static PopupConfig Info(string titulo, string mensagem, Action onOk = null) =>
        new PopupConfig
        {
            Titulo                = string.IsNullOrEmpty(titulo) ? "Aviso" : titulo,
            Mensagem              = mensagem,
            MostrarBotaoConfirmar = true,
            LabelConfirmar        = "OK",
            OnConfirmar           = onOk,
            MostrarIconeInfo      = true,
        };

    public static PopupConfig Sucesso(string titulo, string mensagem, Action onOk = null) =>
        new PopupConfig
        {
            Titulo                = string.IsNullOrEmpty(titulo) ? "Concluído" : titulo,
            Mensagem              = mensagem,
            MostrarBotaoConfirmar = true,
            LabelConfirmar        = "OK",
            OnConfirmar           = onOk,
            MostrarIconeSucesso   = true,
        };

    /// <summary>Popup com um campo de texto editável — ex: renomear algo, editar uma descrição
    /// curta. onConfirmar recebe o texto atual do campo no momento do clique.</summary>
    public static PopupConfig Edicao(
        string titulo, string mensagem,
        string textoInicial,
        Action<string> onConfirmar,
        Action onCancelar   = null,
        string placeholder  = "",
        string labelConfirmar = "Salvar",
        string labelCancelar  = "Cancelar") =>
        new PopupConfig
        {
            Titulo                = titulo,
            Mensagem              = mensagem,
            MostrarBotaoConfirmar = true,
            MostrarBotaoCancelar  = true,
            LabelConfirmar        = labelConfirmar,
            LabelCancelar         = labelCancelar,
            OnCancelar            = onCancelar,
            MostrarInputTexto     = true,
            TextoInicial          = textoInicial ?? "",
            PlaceholderTexto      = placeholder,
            OnTextoConfirmado     = onConfirmar,
            MostrarIconeInfo      = true,
        };

    public static PopupConfig SubirNivel(
        int nivelAtual,
        Action<int> onConfirmar,
        Action onCancelar = null) =>
        new PopupConfig
        {
            Titulo                = "Subir de Nível",
            Mensagem              = "Escolha o novo nível do personagem:",
            MostrarBotaoConfirmar = true,
            MostrarBotaoCancelar  = true,
            LabelConfirmar        = "Confirmar",
            LabelCancelar         = "Cancelar",
            OnCancelar            = onCancelar,
            MostrarDropdownNivel  = true,
            NivelMinDropdown      = nivelAtual + 1,
            OnNivelSelecionado    = onConfirmar,
            MostrarIconeInfo      = true,
        };
}
