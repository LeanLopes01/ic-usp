using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using TMPro;
using UnityEngine.Networking;
using System.Security.Cryptography;
using System.Text;
using System;

public class RegistrationManager : MonoBehaviour
{
    public TMP_InputField usernameInput;
    public TMP_InputField emailInput;
    public TMP_InputField passwordInput;
    public Button registerButton;
    public TMP_Text statusText;

    // Referência para o gerenciador de telas
    public ToggleObjects screenManager;
    
    // Chave secreta para autenticação
    private const string SECRET_KEY = "Un1tyVTT_S3cr3tK3y_2025@";
    private const string API_URL = "https://docjediwho.com.br/database_vtt/register.php";

    void Start()
    {
        registerButton.onClick.AddListener(CallRegister);
        statusText.text = "";
    }

    public void CallRegister()
    {
        StartCoroutine(Register());
    }

    IEnumerator Register()
    {
        string username = usernameInput.text.Trim();
        string email = emailInput.text.Trim();
        string password = passwordInput.text;

        // Validação básica no cliente
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
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
        form.AddField("username", username);
        form.AddField("email", email);
        form.AddField("password", hashedPassword);
        
        // Adiciona timestamp e hash de autenticação
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string authHash = ComputeSHA256Hash($"{SECRET_KEY}{timestamp}");
        form.AddField("timestamp", timestamp.ToString());
        form.AddField("auth_hash", authHash);

        using (UnityWebRequest www = UnityWebRequest.Post(API_URL, form))
        {
            // Timeout de 10 segundos
            www.timeout = 10;
            
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"Erro: {www.error}");
                Debug.LogError($"Resposta: {www.downloadHandler.text}");
                UpdateStatus("Erro de conexão com o servidor.");
            }
            else
            {
                Debug.Log("Resposta completa: " + www.downloadHandler.text);
                
                // Verifica se a resposta é exatamente "sucesso"
                if (www.downloadHandler.text.Trim() == "sucesso")
                {
                    UpdateStatus("Registro concluído com sucesso!");
                    
                    // Espera 2 segundos antes de mudar de tela
                    yield return new WaitForSeconds(2f);
                    
                    // Volta para a tela de login (índice 0)
                    if (screenManager != null)
                    {
                        screenManager.ShowScreen(0);
                    }
                    else
                    {
                        Debug.LogError("Gerenciador de telas não configurado!");
                    }
                    
                    // Limpa os campos
                    usernameInput.text = "";
                    emailInput.text = "";
                    passwordInput.text = "";
                }
                else
                {
                    // Mostra a mensagem de erro do servidor
                    UpdateStatus(www.downloadHandler.text);
                }
            }
        }
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