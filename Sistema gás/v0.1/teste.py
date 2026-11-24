import requests

ESP8266_IP = os.getenv('ESP8266_IP', '192.168.1.100')

try:
    response = requests.get(f"http://{ESP8266_IP}/json")
    if response.status_code == 200:
        print("Dados recebidos:", response.json())
    else:
        print("Erro no servidor:", response.status_code)
except requests.exceptions.RequestException as e:
    print("Erro de conexão:", e)
