using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Security.Cryptography;
using System.Text;
using System;

public class LoginManager : MonoBehaviour
{
    public TMP_InputField emailInput;
    public TMP_InputField passwordInput;
    public Button loginButton;
    public TMP_Text statusText;
    public ToggleObjects screenManager;
    
    // Chave secreta (deve ser a mesma usada no registro)
    private const string SECRET_KEY = "Un1tyVTT_S3cr3tK3y_2025@";
    private const string API_URL = "https://docjediwho.com.br/database_vtt/login.php";

    void Start()
    {
        loginButton.onClick.AddListener(CallLogin);
        statusText.text = "";
    }

    public void CallLogin()
    {
        StartCoroutine(Login());
    }

    IEnumerator Login()
    {
        string email = emailInput.text.Trim();
        string password = passwordInput.text;

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            UpdateStatus("Por favor, preencha todos os campos.");
            yield break;
        }

        if (!IsValidEmail(email))
        {
            UpdateStatus("Formato de email inválido.");
            yield break;
        }

        // Gerar hash SHA256 da senha
        string hashedPassword = ComputeSHA256Hash(password);

        WWWForm form = new WWWForm();
        form.AddField("email", email);
        form.AddField("password", hashedPassword);
        
        // Autenticação
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string authHash = ComputeSHA256Hash($"{SECRET_KEY}{timestamp}");
        form.AddField("timestamp", timestamp.ToString());
        form.AddField("auth_hash", authHash);

        using (UnityWebRequest www = UnityWebRequest.Post(API_URL, form))
        {
            www.timeout = 10;
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Erro: {www.error}");
                UpdateStatus("Erro de conexão com o servidor.");
            }
            else
            {
                Debug.Log("Resposta do servidor: " + www.downloadHandler.text);

                LoginResponse response = null;
                try
                {
                    response = JsonUtility.FromJson<LoginResponse>(www.downloadHandler.text);
                }
                catch (Exception e)
                {
                    Debug.LogError("Erro ao interpretar resposta: " + e.Message);
                    UpdateStatus("Resposta inválida do servidor.");
                }

                if (response != null)
                {
                    if (response.status == "sucesso")
                    {
                        PlayerSession.Instance.email = response.email;
                        PlayerSession.Instance.playerName = response.username;
                        PlayerSession.Instance.playerId = response.id;

                        UpdateStatus("Login bem-sucedido!");
                        yield return new WaitForSeconds(1f);
                        SceneManager.LoadScene("home-screen");
                    }
                    else
                    {
                        UpdateStatus("Erro: " + response.status);
                    }
                }
            }
        }
    }

    public class LoginResponse
    {
        public string status;
        public int id;
        public string username;
        public string email;
    }

    private void UpdateStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
        Debug.Log(message);
    }

    private string ComputeSHA256Hash(string input)
    {
        using (SHA256 sha256 = SHA256.Create())
        {
            byte[] inputBytes = Encoding.UTF8.GetBytes(input);
            byte[] hashBytes = sha256.ComputeHash(inputBytes);
            
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < hashBytes.Length; i++)
            {
                builder.Append(hashBytes[i].ToString("x2"));
            }
            return builder.ToString();
        }
    }

    private bool IsValidEmail(string email)
    {
        try {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch {
            return false;
        }
    }
}