import threading
import queue
import sqlite3
import time
import serial
import sys
import os
from datetime import datetime
import tkinter as tk
from tkinter import ttk, messagebox
from matplotlib.backends.backend_tkagg import FigureCanvasTkAgg
import matplotlib.pyplot as plt

# ---------- CONFIGURAÇÃO ----------
SERIAL_PORT = "COM3"   # ajuste aqui
BAUD = 115200
DB_FILE = "sensor_data.db"

# ---------- BANCO ----------
def init_db():
    conn = sqlite3.connect(DB_FILE)
    c = conn.cursor()
    c.execute('''
        CREATE TABLE IF NOT EXISTS readings (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            timestamp TEXT,
            tempo_s INTEGER,
            mq2 INTEGER,
            mq3 INTEGER,
            mq5 INTEGER,
            temp REAL,
            hum REAL
        )
    ''')
    conn.commit()
    conn.close()

def insert_reading(tempo, mq2, mq3, mq5, temp, hum):
    conn = sqlite3.connect(DB_FILE)
    c = conn.cursor()
    c.execute('''
        INSERT INTO readings (timestamp, tempo_s, mq2, mq3, mq5, temp, hum)
        VALUES (?, ?, ?, ?, ?, ?, ?)
    ''', (datetime.now().isoformat(), tempo, mq2, mq3, mq5, temp, hum))
    conn.commit()
    conn.close()

# ---------- Serial reader thread ----------
class SerialReader(threading.Thread):
    def __init__(self, port, baud, out_queue):
        super().__init__(daemon=True)
        self.port = port
        self.baud = baud
        self.out_queue = out_queue
        self._stop_event = threading.Event()
        self.serial = None

    def connect(self):
        try:
            self.serial = serial.Serial(self.port, self.baud, timeout=1)
            return True
        except Exception as e:
            print("Erro ao abrir serial:", e)
            return False

    def stop(self):
        self._stop_event.set()
        try:
            if self.serial and self.serial.is_open:
                self.serial.close()
        except:
            pass

    def run(self):
        if not self.connect():
            print("Não foi possível conectar à porta serial")
            return
        while not self._stop_event.is_set():
            try:
                line = self.serial.readline().decode(errors='ignore').strip()
                if not line:
                    continue
                parts = line.split(',')
                if len(parts) >= 6 and parts[0].isdigit():
                    try:
                        tempo = int(parts[0])
                        mq2 = int(parts[1])
                        mq3 = int(parts[2])
                        mq5 = int(parts[3])
                        temp = float(parts[4])
                        hum = float(parts[5])
                        self.out_queue.put((tempo, mq2, mq3, mq5, temp, hum))
                    except Exception:
                        pass
                else:
                    print("MSG:", line)
            except Exception as e:
                print("Erro leitura serial:", e)
                time.sleep(0.5)
        if self.serial and self.serial.is_open:
            self.serial.close()

# ---------- GUI ----------
class App:
    def __init__(self, root):
        self.root = root
        root.title("Mega Receiver - Monitor")

        # queue da thread serial
        self.q = queue.Queue()

        # iniciar DB
        init_db()

        # -------- FRAME SUPERIOR --------
        frm = ttk.Frame(root)
        frm.pack(side=tk.TOP, fill=tk.X, padx=8, pady=6)

        self.port_var = tk.StringVar(value=SERIAL_PORT)
        ttk.Label(frm, text="Porta:").pack(side=tk.LEFT)
        self.port_entry = ttk.Entry(frm, width=10, textvariable=self.port_var)
        self.port_entry.pack(side=tk.LEFT, padx=4)

        self.connect_btn = ttk.Button(frm, text="Conectar", command=self.toggle_connect)
        self.connect_btn.pack(side=tk.LEFT, padx=4)

        self.start_btn = ttk.Button(frm, text="Start", command=self.send_start, state=tk.DISABLED)
        self.start_btn.pack(side=tk.LEFT, padx=4)

        self.stop_btn = ttk.Button(frm, text="Stop", command=self.send_stop, state=tk.DISABLED)
        self.stop_btn.pack(side=tk.LEFT, padx=4)

        # -------- FRAME DE ETAPAS --------
        etapas_frame = ttk.LabelFrame(root, text="Tempos das Etapas (ms)")
        etapas_frame.pack(side=tk.TOP, fill=tk.X, padx=8, pady=4)

        self.stage_vars = []
        for i in range(6):
            ttk.Label(etapas_frame, text=f"E{i}:").grid(row=0, column=i*2, padx=2, pady=2)
            var = tk.IntVar(value=10000)
            entry = ttk.Entry(etapas_frame, textvariable=var, width=8)
            entry.grid(row=0, column=i*2+1, padx=2)
            self.stage_vars.append(var)

        self.send_stages_btn = ttk.Button(etapas_frame, text="Enviar tempos", command=self.send_stage_times)
        self.send_stages_btn.grid(row=1, column=0, columnspan=12, pady=4)

        # -------- FRAME DE DELAYS --------
        delays_frame = ttk.Frame(root)
        delays_frame.pack(side=tk.TOP, fill=tk.X, padx=8, pady=4)
        ttk.Label(delays_frame, text="Leitura (ms):").pack(side=tk.LEFT, padx=(10,2))
        self.read_delay_var = tk.IntVar(value=200)
        ttk.Entry(delays_frame, width=6, textvariable=self.read_delay_var).pack(side=tk.LEFT)
        ttk.Label(delays_frame, text="Plot (ms):").pack(side=tk.LEFT, padx=(10,2))
        self.plot_delay_var = tk.IntVar(value=1000)
        ttk.Entry(delays_frame, width=6, textvariable=self.plot_delay_var).pack(side=tk.LEFT)
        ttk.Button(delays_frame, text="Aplicar", command=self.update_timers).pack(side=tk.LEFT, padx=6)

        # -------- STATUS --------
        values_frame = ttk.Frame(root)
        values_frame.pack(side=tk.TOP, fill=tk.X, padx=8)
        self.status_var = tk.StringVar(value="Desconectado")
        ttk.Label(values_frame, textvariable=self.status_var).pack(side=tk.LEFT)

        # -------- PLOT --------
        self.fig, self.ax = plt.subplots(figsize=(7,3))
        self.canvas = FigureCanvasTkAgg(self.fig, master=root)
        self.canvas.get_tk_widget().pack(side=tk.TOP, fill=tk.BOTH, expand=True)

        self.times, self.mq2s, self.mq3s, self.mq5s, self.temps, self.hums = ([] for _ in range(6))

        self.serial_thread = None
        self.serial_port = None

        self.read_delay = 200
        self.plot_delay = 1000

        self.root.after(self.read_delay, self.process_queue)
        self.root.after(self.plot_delay, self.update_plot)

    # ---------- Funções de interface ----------
    def update_timers(self):
        try:
            self.read_delay = max(50, int(self.read_delay_var.get()))
            self.plot_delay = max(100, int(self.plot_delay_var.get()))
            self.status_var.set(f"Atualizações: leitura {self.read_delay}ms | plot {self.plot_delay}ms")
        except ValueError:
            messagebox.showerror("Erro", "Digite apenas números inteiros para os tempos.")

    def toggle_connect(self):
        if self.serial_thread is None:
            port = self.port_var.get()
            self.q.queue.clear()
            self.serial_thread = SerialReader(port, BAUD, self.q)
            self.serial_thread.start()
            self.connect_btn.config(text="Desconectar")
            self.start_btn.config(state=tk.NORMAL)
            self.stop_btn.config(state=tk.NORMAL)
            self.status_var.set(f"Conectado {port}")
            time.sleep(0.2)
            try:
                self.serial_port = self.serial_thread.serial
            except:
                self.serial_port = None
        else:
            self.serial_thread.stop()
            self.serial_thread = None
            self.connect_btn.config(text="Conectar")
            self.start_btn.config(state=tk.DISABLED)
            self.stop_btn.config(state=tk.DISABLED)
            self.status_var.set("Desconectado")
            self.serial_port = None

    def send_start(self):
        self._send_serial_command("START\n")

    def send_stop(self):
        self._send_serial_command("STOP\n")

    def _send_serial_command(self, cmd):
        if self.serial_thread and self.serial_thread.serial and self.serial_thread.serial.is_open:
            try:
                self.serial_thread.serial.write(cmd.encode())
            except Exception as e:
                messagebox.showerror("Erro", f"Falha ao enviar comando '{cmd.strip()}': {e}")
        else:
            messagebox.showwarning("Aviso", "Não há conexão serial ativa.")

    def send_stage_times(self):
        """Envia os tempos de cada etapa via SET <etapa> <tempo_ms>."""
        if not (self.serial_thread and self.serial_thread.serial and self.serial_thread.serial.is_open):
            messagebox.showwarning("Aviso", "Conecte-se primeiro antes de enviar os tempos.")
            return

        for i, var in enumerate(self.stage_vars):
            try:
                tempo = int(var.get())
                cmd = f"SET {i} {tempo}\n"
                self.serial_thread.serial.write(cmd.encode())
                time.sleep(0.1)
            except Exception as e:
                messagebox.showerror("Erro", f"Falha ao enviar etapa {i}: {e}")
                return

        self.status_var.set("Tempos enviados com sucesso!")

    # ---------- Processamento de dados ----------
    def process_queue(self):
        while not self.q.empty():
            tempo, mq2, mq3, mq5, temp, hum = self.q.get()
            insert_reading(tempo, mq2, mq3, mq5, temp, hum)
            self.times.append(tempo)
            self.mq2s.append(mq2)
            self.mq3s.append(mq3)
            self.mq5s.append(mq5)
            self.temps.append(temp)
            self.hums.append(hum)
            MAXPTS = 200
            if len(self.times) > MAXPTS:
                for arr in (self.times, self.mq2s, self.mq3s, self.mq5s, self.temps, self.hums):
                    del arr[0:len(self.times)-MAXPTS]
        self.root.after(self.read_delay, self.process_queue)

    def update_plot(self):
        self.ax.cla()
        ax1 = self.ax
        if self.times:
            ax1.plot(self.times, self.mq2s, label='MQ2')
            ax1.plot(self.times, self.mq3s, label='MQ3')
            ax1.plot(self.times, self.mq5s, label='MQ5')
            ax1.set_ylabel('ADC')
            ax1.legend(loc='upper left')

            ax2 = ax1.twinx()
            ax2.plot(self.times, self.temps, label='Temp (C)', linestyle='--')
            ax2.plot(self.times, self.hums, label='Hum (%)', linestyle=':')
            ax2.set_ylabel('Temp / Hum')

            ax1.set_xlabel('Tempo (s)')
            self.fig.tight_layout()
            self.canvas.draw()

        self.root.after(self.plot_delay, self.update_plot)

# ---------- MAIN ----------
if __name__ == "__main__":
    root = tk.Tk()
    app = App(root)
    root.mainloop()
