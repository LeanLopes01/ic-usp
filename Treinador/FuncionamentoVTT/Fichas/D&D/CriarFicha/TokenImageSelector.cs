using UnityEngine;
using UnityEngine.UI;
using System;
using System.IO;
using TMPro;

public class TokenImageSelector : MonoBehaviour
{
    [Header("Componentes UI")]
    public Button botaoSelecionarImagem;
    public Image imagemTokenDisplay;
    public TextMeshProUGUI textoNomeArquivo;
    
    [Header("UI - Alternativa (Input de caminho)")]
    public InputField caminhoImagemInput;
    public Button botaoCarregarDoCaminho;
    
    private Texture2D imagemCarregada;
    private string ultimoCaminhoUtilizado = "";
    private int _ultimaFichaIdCarregada = -1;
    private Sprite _spriteDefault;
    private Color _corDefault;

    private void Awake()
    {
        if (imagemTokenDisplay != null)
        {
            _spriteDefault = imagemTokenDisplay.sprite;
            _corDefault = imagemTokenDisplay.color;
        }
    }

    private void OnEnable()
    {
        if (botaoSelecionarImagem != null)
        {
            botaoSelecionarImagem.onClick.RemoveListener(AbrirSelecionarImagem);
            botaoSelecionarImagem.onClick.AddListener(AbrirSelecionarImagem);
        }

        if (botaoCarregarDoCaminho != null)
        {
            botaoCarregarDoCaminho.onClick.RemoveListener(CarregarDoCaminhoInput);
            botaoCarregarDoCaminho.onClick.AddListener(CarregarDoCaminhoInput);
        }

        bool fichaIdMudou = _ultimaFichaIdCarregada != GlobalFichaData.fichasDndId;
        if (fichaIdMudou)
        {
            Debug.Log($"[TokenImageSelector] OnEnable — ficha mudou ({_ultimaFichaIdCarregada} → {GlobalFichaData.fichasDndId}). Limpando token anterior.");
            _ultimaFichaIdCarregada = GlobalFichaData.fichasDndId;
            GlobalFichaData.caminhoImagemToken = "";
            LimparImagemDisplay();

            // Carrega token do snapshot da nova ficha, se houver
            int playerId = DataManager.Instance != null ? DataManager.Instance.GetCurrentId() : 0;
            string steamId = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";
            if (LocalFichaStorage.TryGetFichaSnapshot(steamId, playerId, GlobalFichaData.fichasGeneralId, GlobalFichaData.fichasDndId, out LocalFichaData snap)
                && !string.IsNullOrEmpty(snap.token_image_path))
            {
                Debug.Log($"[TokenImageSelector] Carregando token do snapshot: {snap.token_image_path}");
                CarregarImagemDoArquivo(snap.token_image_path);
            }
        }
        else if (!string.IsNullOrEmpty(GlobalFichaData.caminhoImagemToken))
        {
            CarregarImagemDoArquivo(GlobalFichaData.caminhoImagemToken);
        }
    }

    private void OnDisable()
    {
        if (botaoSelecionarImagem != null)
        {
            botaoSelecionarImagem.onClick.RemoveListener(AbrirSelecionarImagem);
        }

        if (botaoCarregarDoCaminho != null)
        {
            botaoCarregarDoCaminho.onClick.RemoveListener(CarregarDoCaminhoInput);
        }
    }

    public void AbrirSelecionarImagem()
    {
        AbrirSeletorArquivo();
    }

    private void AbrirSeletorArquivo()
    {
        #if UNITY_EDITOR
        string caminho = UnityEditor.EditorUtility.OpenFilePanel("Selecionar imagem do token/personagem", "", "png,jpg,jpeg,bmp,gif");
        if (string.IsNullOrWhiteSpace(caminho))
            return;

        if (!File.Exists(caminho))
        {
            Debug.LogWarning("[TokenImageSelector] Caminho de imagem inválido.");
            return;
        }

        CarregarImagemDoArquivo(caminho);
        #else
        Debug.LogWarning("[TokenImageSelector] Importação por seletor de arquivo está disponível apenas no Editor Unity sem plugins externos.");
        MostrarAlternativaInput();
        #endif
    }

    private string ObterCaminhoInicial()
    {
        // Tentar Pictures > Documents > Desktop
        string pictures = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Pictures");
        if (Directory.Exists(pictures)) return pictures;

        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (Directory.Exists(documents)) return documents;

        return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
    }

    private void TentarAbrirDialogoWindows(string caminhoInicial)
    {
        try
        {
            // Método sem usar dynamic - usando apenas tipos públicos
            var assemblyName = new System.Reflection.AssemblyName("System.Windows.Forms");
            var assembly = System.Reflection.Assembly.Load(assemblyName);
            
            var openFileDialogType = assembly.GetType("System.Windows.Forms.OpenFileDialog");
            if (openFileDialogType == null)
            {
                MostrarAlternativaInput();
                return;
            }

            object dialog = Activator.CreateInstance(openFileDialogType);
            
            // Setar propriedades via reflection
            var titleProperty = openFileDialogType.GetProperty("Title");
            var initialDirProperty = openFileDialogType.GetProperty("InitialDirectory");
            var filterProperty = openFileDialogType.GetProperty("Filter");
            var filterIndexProperty = openFileDialogType.GetProperty("FilterIndex");
            var restoreDirProperty = openFileDialogType.GetProperty("RestoreDirectory");
            var fileNameProperty = openFileDialogType.GetProperty("FileName");
            var showDialogMethod = openFileDialogType.GetMethod("ShowDialog", System.Type.EmptyTypes);

            if (titleProperty != null) titleProperty.SetValue(dialog, "Selecionar imagem do token/personagem");
            if (initialDirProperty != null) initialDirProperty.SetValue(dialog, caminhoInicial);
            if (filterProperty != null) filterProperty.SetValue(dialog, "Arquivos de Imagem (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Todos os arquivos (*.*)|*.*");
            if (filterIndexProperty != null) filterIndexProperty.SetValue(dialog, 1);
            if (restoreDirProperty != null) restoreDirProperty.SetValue(dialog, true);

            // Chamar ShowDialog
            if (showDialogMethod != null)
            {
                object resultado = showDialogMethod.Invoke(dialog, null);
                
                // Verificar se resultado == DialogResult.OK (valor 1)
                if (resultado != null && (int)resultado == 1)
                {
                    if (fileNameProperty != null)
                    {
                        string caminhoSelecionado = fileNameProperty.GetValue(dialog) as string;
                        if (!string.IsNullOrEmpty(caminhoSelecionado) && File.Exists(caminhoSelecionado))
                        {
                            CarregarImagemDoArquivo(caminhoSelecionado);
                            GlobalFichaData.caminhoImagemToken = caminhoSelecionado;
                            ultimoCaminhoUtilizado = caminhoSelecionado;
                        }
                    }
                }
            }

            // Limpar
            var disposeMethod = openFileDialogType.GetMethod("Dispose", System.Type.EmptyTypes);
            if (disposeMethod != null)
            {
                disposeMethod.Invoke(dialog, null);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[TokenImageSelector] Não foi possível carregar System.Windows.Forms: " + ex.Message);
            MostrarAlternativaInput();
        }
    }

    private void MostrarAlternativaInput()
    {
        Debug.Log("[TokenImageSelector] Use o campo de texto abaixo para colar o caminho da imagem ou clique em 'Carregar do Caminho'.");
        if (caminhoImagemInput != null)
        {
            caminhoImagemInput.gameObject.SetActive(true);
        }
    }

    private void CarregarDoCaminhoInput()
    {
        if (caminhoImagemInput == null)
        {
            Debug.LogWarning("[TokenImageSelector] Campo de input não configurado.");
            return;
        }

        string caminho = caminhoImagemInput.text.Trim();
        if (string.IsNullOrEmpty(caminho))
        {
            Debug.LogWarning("[TokenImageSelector] Por favor, insira um caminho válido.");
            return;
        }

        CarregarImagemDoArquivo(caminho);
        if (!string.IsNullOrEmpty(GlobalFichaData.caminhoImagemToken))
        {
            caminhoImagemInput.gameObject.SetActive(false);
        }
    }

    private void CarregarImagemDoArquivo(string caminho)
    {
        if (!File.Exists(caminho))
        {
            Debug.LogWarning("[TokenImageSelector] Arquivo não encontrado: " + caminho);
            LimparImagemDisplay();
            return;
        }

        try
        {
            string caminhoPersistido = PersistirCopiaLocal(caminho);
            byte[] bytesImagem = File.ReadAllBytes(caminhoPersistido);
            imagemCarregada = new Texture2D(1, 1);
            if (imagemCarregada.LoadImage(bytesImagem))
            {
                GlobalFichaData.caminhoImagemToken = caminhoPersistido;
                PersistirCaminhoNoSnapshot(caminhoPersistido);

                // Exibir imagem se houver componente Image
                if (imagemTokenDisplay != null)
                {
                    Sprite sprite = Sprite.Create(imagemCarregada,
                        new Rect(0, 0, imagemCarregada.width, imagemCarregada.height),
                        Vector2.one * 0.5f);
                    imagemTokenDisplay.sprite = sprite;
                    imagemTokenDisplay.color = Color.white;
                    imagemTokenDisplay.gameObject.SetActive(true);
                }

                // Exibir nome do arquivo se houver TextMeshPro
                if (textoNomeArquivo != null)
                {
                    string nomeArquivo = Path.GetFileName(caminho);
                    textoNomeArquivo.text = $"Token: {nomeArquivo}";
                    textoNomeArquivo.gameObject.SetActive(true);
                }

                Debug.Log("[TokenImageSelector] Imagem carregada com sucesso: " + caminho);
            }
            else
            {
                Debug.LogWarning("[TokenImageSelector] Falha ao carregar imagem de: " + caminho);
                LimparImagemDisplay();
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("[TokenImageSelector] Erro ao carregar imagem: " + ex.Message);
            LimparImagemDisplay();
        }
    }

    private void LimparImagemDisplay()
    {
        if (imagemTokenDisplay != null)
        {
            imagemTokenDisplay.sprite = _spriteDefault;
            imagemTokenDisplay.color = _spriteDefault != null ? _corDefault : Color.clear;
        }

        if (textoNomeArquivo != null)
        {
            textoNomeArquivo.text = "Nenhuma imagem selecionada";
            textoNomeArquivo.gameObject.SetActive(true);
        }

        imagemCarregada = null;
        GlobalFichaData.caminhoImagemToken = "";
    }

    private string PersistirCopiaLocal(string caminhoOrigem)
    {
        try
        {
            string origemNormalizada = Path.GetFullPath(caminhoOrigem);
            string rootTokens = Path.Combine(Application.persistentDataPath, "fichas", "tokens");
            Directory.CreateDirectory(rootTokens);

            string rootNormalizado = Path.GetFullPath(rootTokens);
            if (origemNormalizada.StartsWith(rootNormalizado, StringComparison.OrdinalIgnoreCase))
            {
                return origemNormalizada;
            }

            int fichaId = Mathf.Max(1, GlobalFichaData.fichasDndId);
            string extensao = Path.GetExtension(origemNormalizada);
            if (string.IsNullOrWhiteSpace(extensao)) extensao = ".png";

            string destino = Path.Combine(rootTokens, $"token_{fichaId}{extensao}");
            File.Copy(origemNormalizada, destino, true);
            return destino;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[TokenImageSelector] Falha ao persistir cópia local da imagem: " + ex.Message);
            return caminhoOrigem;
        }
    }

    private void PersistirCaminhoNoSnapshot(string caminho)
    {
        if (string.IsNullOrWhiteSpace(caminho)) return;
        if (GlobalFichaData.fichasDndId <= 0) return;

        int playerId = DataManager.Instance != null ? DataManager.Instance.GetCurrentId() : 0;
        string steamId = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";

        LocalFichaStorage.SaveTokenImagePath(
            steamId,
            playerId,
            GlobalFichaData.fichasGeneralId,
            GlobalFichaData.fichasDndId,
            caminho
        );
    }

    /// <summary>
    /// Define a imagem diretamente via código
    /// </summary>
    public void DefinirImagemDoArquivo(string caminho)
    {
        CarregarImagemDoArquivo(caminho);
    }

    /// <summary>
    /// Retorna o caminho da imagem selecionada
    /// </summary>
    public string ObterCaminhoImagemSelecionada()
    {
        return GlobalFichaData.caminhoImagemToken;
    }

    /// <summary>
    /// Retorna a textura carregada
    /// </summary>
    public Texture2D ObterTexturaCarregada()
    {
        return imagemCarregada;
    }

    /// <summary>
    /// Limpa a imagem selecionada
    /// </summary>
    public void LimparSelecao()
    {
        LimparImagemDisplay();
    }
}
