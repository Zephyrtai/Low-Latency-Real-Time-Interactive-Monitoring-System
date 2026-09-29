# Toolbox
from secrets import UDP_IP, wifi_name, password
from machine import Pin
import machine
import json
import network
import socket
import time
import random
import sys


wifi_name = "YOUR_WIFI_NAME"
password = "YOUR_PASSWORD"

# 1. Connect to wifi
def wifi_connect():
    wlan = network.WLAN(network.STA_IF)
    wlan.active(True)
    wlan.connect(wifi_name, password)

    timeout = 10
    while not wlan.isconnected() and timeout > 0:
        print("Connecting to Wifi...")
        time.sleep(1)
        timeout -= 1

    if not wlan.isconnected():
        print("Failed to connect! Stopping...")
        sys.exit()

    print("Connected! IP: ", wlan.ifconfig()[0])

# 2. The Delivery Method
UDP_IP = "YOUR_UDP_IP"
UDP_PORT = 5005

# Create a UDP Socket
sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
sock.setblocking(False) # set Non-Blocking mode

# 3. Collect data
# ①.Initialize
launch = Pin(3, Pin.OUT)
echo = Pin(2, Pin.IN)

start_time = 0
duration = 0
last_dis = 0.0
data_ready = False

# Interrupt ReQuest: called when Echo pin changes state 
def echo_handler(pin):
    global start_time, duration, data_ready

    if pin.value() == 1:
        start_time = time.ticks_us()
    else:
        end_time = time.ticks_us()
        duration = time.ticks_diff(end_time, start_time)
        data_ready = True

# ②.【Bound Interrupt】: Trigger on both rising and falling edges
echo.irq(trigger=Pin.IRQ_RISING | Pin.IRQ_FALLING, handler = echo_handler)

def trigger_sensor():
    global data_ready
    data_ready = False

    # Trigger distance measurement
    launch.low()
    time.sleep_us(2)
    launch.high()
    time.sleep_us(10)
    launch.low()

# 4. The heartbeat (Send data)
last_send_tick = time.ticks_ms()
last_trigger_tick = time.ticks_ms()

# 5. Package ID
package_id = 0

def package_ID():
    global package_id
    package_id += 1

# ---Implementation---

wifi_connect()

while True:
    current_tick = time.ticks_ms()

    # 1. Trigger on sensor(every 100ms once)
    if time.ticks_diff(current_tick, last_trigger_tick) >= 200:
        trigger_sensor()
        last_trigger_tick = current_tick

    # 2. Check whether the data has been updated by Interrupt ReQuest
    if data_ready:
        state = machine.disable_irq()
        current_duration = duration
        data_ready = False
        machine.enable_irq(state)

        last_dis = current_duration * 0.034 / 2

    # 3. Send data
    if time.ticks_diff(current_tick, last_send_tick) >= 200:# 20Hz
        data = {
            "package_id": package_ID(),
            "speed": round(random.uniform(0,30), 2),
            "raw_distance": round(last_dis, 2)
        }

        message = json.dumps(data)
       
        try:
            sock.sendto(message.encode(), (UDP_IP, UDP_PORT))
            print("Sent:", message)
        except Exception as e:
            
            print("Network bussy, skipping this packet.")
            pass