using UnityEngine;

/// <summary>Impede que o GameObject do NetworkManager (com FacepunchTransport) seja recriado a cada
/// SceneManager.LoadScene (ex: ToggleObjects.RecarregarAplicativoTotal). Sem isto, o objeto vem
/// "de fábrica" na cena e é destruído/recriado a cada reload — e FacepunchTransport.Awake() (pacote
/// de terceiros, Library/PackageCache/com.community.netcode.transport.facepunch, sem nenhuma guarda
/// de SteamClient.IsValid) chama SteamClient.Init() de novo incondicionalmente. Como
/// Perfil/SteamManager.cs já é dono persistente da sessão Steam do app inteiro (Init uma única vez no
/// login, só dá Shutdown em OnApplicationQuit), o SteamClient já está sempre válido nesse ponto —
/// gerando sempre "Calling SteamClient.Init but is already initialized" (engolido pelo try/catch do
/// pacote, mas nunca some do log).
///
/// [DefaultExecutionOrder] bem negativo garante que ESTE Awake rode antes de qualquer outro script no
/// mesmo GameObject (incluindo FacepunchTransport), sem precisar mexer em Project Settings > Script
/// Execution Order. DestroyImmediate (não Destroy) é necessário aqui especificamente porque Destroy
/// só remove o objeto no FIM do frame — tarde demais pra impedir o Awake dos componentes irmãos no
/// mesmo GameObject, que já teria disparado o SteamClient.Init duplicado de qualquer forma.
///
/// Passo manual no Editor: adicionar este componente ao MESMO GameObject que tem NetworkManager e
/// FacepunchTransport na cena.</summary>
[DefaultExecutionOrder(-1000)]
public class NetworkManagerPersistente : MonoBehaviour
{
    private static NetworkManagerPersistente instancia;

    private void Awake()
    {
        if (instancia != null && instancia != this)
        {
            DestroyImmediate(gameObject);
            return;
        }

        instancia = this;

        if (transform.parent == null)
            DontDestroyOnLoad(gameObject);
        else
            Debug.LogWarning("[NetworkManagerPersistente] Está em objeto filho. DontDestroyOnLoad ignorado — mova o NetworkManager para a raiz da cena.");
    }
}
