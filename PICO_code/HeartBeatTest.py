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
import errno


# 1. Connect to wifi
def wifi_connect():
    # wifi_name = "YOUR_WIFI_NAME"
    # password = "YOUR_PASSWORD"

    wlan = network.WLAN(network.STA_IF)
    wlan.active(True)

    wlan.config(pm=network.WLAN.PM_NONE)  # Disable Wi-Fi power management

    # Check "is connected"
    connected_status = wlan.isconnected() # True / False
    print("WI-FI connected at start: ", connected_status)

    if not connected_status:
        wlan.connect(wifi_name, password)

        timeout = 10
        while not wlan.isconnected() and timeout > 0:
            print("Connecting to Wifi...")
            time.sleep(1)
            timeout -= 1

        if not wlan.isconnected():
            raise RuntimeError("Wi-Fi connection timed out")

    print("Connected! Sender_IP: ", wlan.ifconfig()[0])

# 2. The Delivery Method
# UDP_IP = "YOUR_UDP_IP"
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

# 5. Packet ID
packet_id = 0

def packet_ID():
    global packet_id
    packet_id += 1
    return packet_id


# ---Implementation---

wifi_connect()

print("UDP target:", UDP_IP, UDP_PORT)

pending = {}
check_timeout_us = 1000000
timeout_count = 0

try:
    while True:
        current_tick = time.ticks_ms()

        # 1. Trigger on sensor
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
        if time.ticks_diff(current_tick, last_send_tick) >= 200:# 5Hz(Configured transmission rate)
            last_send_tick = current_tick

            packet_id = packet_ID()

            data = {
                "packet_id": packet_id,
                #"speed": round(random.uniform(0,30), 2),
                "raw_distance": round(last_dis, 2)
            }

            message = json.dumps(data) # *Dump to String*
            bytes = message.encode()
            # Time at sending
            pending[packet_id] = time.ticks_us() # **After Encode**
            
            try:
                sock.sendto(bytes, (UDP_IP, UDP_PORT)) # Encode(to bytes)
                print("Sent: ", message)
            except OSError as e:
                # Delete data in pending that sending fault
                pending.pop(packet_id)
                print(e)

        # 4. Check for reply on every loop
        try:
            reply, address = sock.recvfrom(512)
            received_us = time.ticks_us()
        except OSError as e:
            if e.args[0] == errno.EAGAIN: # Returns None
                pass # No packet is available yet.
            else:
                print("Socket error during receive:", e)
        else:
            try:  # Json string ➡ dict
                reply_data = json.loads(reply.decode("utf-8"))
                #loads = load string:Load from String
                if isinstance(reply_data, dict) and reply_data.get("type") == "checking":
                    checking_id = reply_data.get("packet_id")

                    if isinstance(checking_id, int) and checking_id in pending:
                        sent_us = pending.pop(checking_id)
                        cost_us= time.ticks_diff(received_us, sent_us)

                        if cost_us < check_timeout_us:
                            rtt_ms = cost_us / 1000
                            print("RTT: ", rtt_ms, "ms | ID: ", checking_id)
                        else:
                            timeout_count += 1
                            print("Late checking: ", checking_id)
            except ValueError:
                print("Invaild checking JSON")

        # 5. Remove records that timeout
        now_us = time.ticks_us()

        for pending_id in list(pending): #pending_id: avoid to override 'packet_id'
            if time.ticks_diff(now_us, pending[pending_id]) >= check_timeout_us:
                pending.pop(pending_id)
                timeout_count += 1
                print("Checking timeout: ", pending_id)

except KeyboardInterrupt:
    print("Test stopped.")

finally:
    print("Total checking timeout: ", timeout_count)
    print("Pending checking at stop: ", len(pending))
    print(pending)
    sock.close()