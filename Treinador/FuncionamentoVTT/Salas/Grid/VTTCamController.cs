using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

[RequireComponent(typeof(Camera))]
public class VTTCamController : MonoBehaviour
{
    [Header("Pan")]
    [SerializeField] private float panSpeed = 0.015f;

    [Header("Zoom")]
    [SerializeField] private float zoomSpeed = 0.75f;
    [SerializeField] private float minZoom = 0.5f;
    [SerializeField] private float maxZoom = 200f;

    [Header("Limites")]
    [SerializeField] private float margemLimitePadrao = 2f;

    private Camera cam;
    private Vector2 posicaoMouseAnterior;
    private Bounds limitesMovimento;
    private bool possuiLimitesMovimento;
    private bool panBloqueadoPorUI;

    // Touch (celular) — pan de 1 dedo, pinça de 2 dedos pra zoom. Independente do Mouse/scroll
    // acima (dispositivo diferente no Input System), então não interfere no fluxo desktop.
    private int touchIdPan = -1;
    private Vector2 posicaoTouchAnterior;
    private float distanciaPinchAnterior = -1f;

    private void Awake()
    {
        cam = GetComponent<Camera>();
        if (cam != null)
            cam.orthographic = true;
    }

    private void Update()
    {
        HandlePan();
        HandleZoom();
        HandleTouch();
        AplicarLimitesDePosicao();
    }

    /// <summary>Pan de 1 dedo / pinça de 2 dedos pra zoom — equivalente touch de HandlePan/HandleZoom
    /// (que usam botão direito/meio do mouse e scroll, sem efeito num celular sem mouse).</summary>
    private void HandleTouch()
    {
        Touchscreen tela = Touchscreen.current;
        if (tela == null || cam == null) return;

        TouchControl t0 = null, t1 = null;
        foreach (TouchControl t in tela.touches)
        {
            if (!t.press.isPressed) continue;
            if (t0 == null) t0 = t;
            else if (t1 == null) t1 = t;
        }

        if (t0 != null && t1 != null)
        {
            touchIdPan = -1; // 2 dedos: pausa o pan de 1 dedo enquanto belisca

            float distanciaAtual = Vector2.Distance(t0.position.ReadValue(), t1.position.ReadValue());
            if (distanciaPinchAnterior > 0f && distanciaAtual > 0f)
            {
                float fatorZoom = distanciaPinchAnterior / distanciaAtual;
                float novoZoom = cam.orthographicSize * fatorZoom;
                cam.orthographicSize = Mathf.Clamp(novoZoom, minZoom, maxZoom);
            }
            distanciaPinchAnterior = distanciaAtual;
            return;
        }

        distanciaPinchAnterior = -1f;

        if (t0 == null)
        {
            touchIdPan = -1;
            return;
        }

        int idAtual = t0.touchId.ReadValue();
        Vector2 posicaoAtual = t0.position.ReadValue();

        if (touchIdPan != idAtual)
        {
            // Início de um toque novo — só grava a posição, sem aplicar delta (evita um "pulo"
            // usando a posição de um toque anterior diferente).
            touchIdPan = idAtual;
            posicaoTouchAnterior = posicaoAtual;
            return;
        }

        Vector2 delta = posicaoAtual - posicaoTouchAnterior;
        posicaoTouchAnterior = posicaoAtual;
        if (delta == Vector2.zero) return;

        float escala = cam.orthographicSize * 2f / Screen.height;
        Vector3 deslocamento = new Vector3(-delta.x, -delta.y, 0f) * escala * panSpeed * 60f;
        transform.position += deslocamento;
    }

    private void HandlePan()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null)
            return;

        bool segurandoPan = mouse.rightButton.isPressed || mouse.middleButton.isPressed;

        if (!segurandoPan)
        {
            posicaoMouseAnterior = mouse.position.ReadValue();
            panBloqueadoPorUI = false;
            return;
        }

        // Verifica bloqueio por UI apenas no primeiro frame do drag
        bool primeiroFrame = mouse.rightButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame;
        if (primeiroFrame)
            panBloqueadoPorUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        if (panBloqueadoPorUI)
        {
            posicaoMouseAnterior = mouse.position.ReadValue();
            return;
        }

        Vector2 posicaoAtual = mouse.position.ReadValue();
        Vector2 delta = posicaoAtual - posicaoMouseAnterior;
        posicaoMouseAnterior = posicaoAtual;

        if (delta == Vector2.zero)
            return;

        // Converte o delta de pixels para unidades de mundo usando o orthographicSize
        float escala = cam.orthographicSize * 2f / Screen.height;
        Vector3 deslocamento = new Vector3(-delta.x, -delta.y, 0f) * escala * panSpeed * 60f;
        transform.position += deslocamento;
    }

    private void HandleZoom()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || cam == null)
            return;

        float raw = mouse.scroll.ReadValue().y;
        if (Mathf.Approximately(raw, 0f))
            return;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        // O Input System pode retornar ~120/notch (raw Windows) ou ~1/notch (normalizado).
        // Detectamos a magnitude para normalizar de forma robusta em ambos os casos.
        float normalizado = Mathf.Abs(raw) >= 10f ? raw / 120f : raw;
        float fatorZoom = 1f - normalizado * zoomSpeed * 0.1f;
        fatorZoom = Mathf.Clamp(fatorZoom, 0.85f, 1.15f);
        float novoZoom = cam.orthographicSize * fatorZoom;
        cam.orthographicSize = Mathf.Clamp(novoZoom, minZoom, maxZoom);
    }

    /// <summary>
    /// Centraliza a câmera no SpriteRenderer e ajusta orthographicSize para enquadrar
    /// o mapa inteiro. Atualiza automaticamente os limites de zoom min/max.
    /// </summary>
    public void FitToSprite(SpriteRenderer sr)
    {
        if (sr == null || sr.sprite == null || cam == null)
            return;

        Vector2 tamanhoLocal = sr.sprite.bounds.size;
        Vector3 escala = sr.transform.lossyScale;
        float largura = Mathf.Abs(tamanhoLocal.x * escala.x);
        float altura = Mathf.Abs(tamanhoLocal.y * escala.y);

        if (largura < 0.001f || altura < 0.001f)
            return;

        // Centraliza no mapa
        Vector3 pos = sr.transform.position;
        transform.position = new Vector3(pos.x, pos.y, transform.position.z);

        // Calcula o orthographicSize necessário para ver o mapa inteiro (margem de 5%)
        float aspect       = cam.aspect > 0f ? cam.aspect : (float)Screen.width / Mathf.Max(1, Screen.height);
        float tamPorAltura = altura  * 0.5f * 1.05f;
        float tamPorLargura = (largura / aspect) * 0.5f * 1.05f;
        float tamanhoIdeal  = Mathf.Max(tamPorAltura, tamPorLargura);

        // Mantém o zoom utilizável sem saltos extremos ao reenquadrar o mapa.
        minZoom = Mathf.Max(0.25f, tamanhoIdeal * 0.15f);
        maxZoom = Mathf.Max(minZoom + 1f, tamanhoIdeal * 1.35f);

        cam.orthographicSize = tamanhoIdeal;
        AplicarLimitesDePosicao();
    }

    /// <summary>Move a câmera pra centralizar numa posição de mundo específica (ex: um token),
    /// preservando zoom/rotação atuais — diferente de FitToSprite, que reenquadra o mapa inteiro.
    /// Respeita os limites de movimento já definidos (ver AplicarLimitesDePosicao), então não sai
    /// pra fora da área do mapa mesmo pedindo pra centralizar num ponto perto da borda.</summary>
    public void CentralizarEm(Vector3 posicaoMundo)
    {
        transform.position = new Vector3(posicaoMundo.x, posicaoMundo.y, transform.position.z);
        AplicarLimitesDePosicao();
    }

    public void DefinirLimitesMovimento(Bounds bounds, float margemExtra = -1f)
    {
        float margem = margemExtra >= 0f ? margemExtra : margemLimitePadrao;
        limitesMovimento = bounds;
        limitesMovimento.Expand(new Vector3(margem * 2f, margem * 2f, 0f));
        possuiLimitesMovimento = true;
        AplicarLimitesDePosicao();
    }

    public void LimparLimitesMovimento()
    {
        possuiLimitesMovimento = false;
    }

    private void AplicarLimitesDePosicao()
    {
        if (!possuiLimitesMovimento || cam == null)
            return;

        float metadeAltura = cam.orthographicSize;
        float metadeLargura = metadeAltura * cam.aspect;
        Vector3 posicao = transform.position;

        float minX = limitesMovimento.min.x + metadeLargura;
        float maxX = limitesMovimento.max.x - metadeLargura;
        float minY = limitesMovimento.min.y + metadeAltura;
        float maxY = limitesMovimento.max.y - metadeAltura;

        posicao.x = minX <= maxX
            ? Mathf.Clamp(posicao.x, minX, maxX)
            : limitesMovimento.center.x;

        posicao.y = minY <= maxY
            ? Mathf.Clamp(posicao.y, minY, maxY)
            : limitesMovimento.center.y;

        transform.position = posicao;
    }
}
