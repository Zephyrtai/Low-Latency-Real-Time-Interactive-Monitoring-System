# Toolbox
from secrets import UDP_IP, wifi_name, password
from machine import Pin
import machine
import json
import network
import socket
import time
import errno
from array import array
import os

# Experiment setting. this interval controls BOTH sensing & sending.
SEND_INTERVAL_MS = 100 # 5Hz=200; 10Hz=100 (Configured transmission rate)
CHECK_TIMEOUT_US = 1000000
MAX_LOG_ROWS = 2000  # 24,000 bytes of row storage on the Pico W. 2000x3x4=24000
PRINT_EACH_PACKET = False
UDP_PORT = 5005


class RttLog:
    # One outcome per attempted packet, indexed by packet_id (starting at 1).
    PENDING = 0
    OK = 1
    TIMEOUT = 2
    LATE_ACK = 3
    SEND_ERROR = 4
    STATUS_NAMES = ("pending_at_stop", "ok", "timeout", "late_ack", "send_error")

    def __init__(self, interval_ms, max_rows):
        self.interval_ms = interval_ms
        self.max_rows = max_rows
        # Each row: send-relative time (ms), observed RTT (us), status code.
        self.rows = array("I")
        for _ in range(max_rows * 3):
            self.rows.append(0) # 2000x3x4=24000
        self.attempted = 0
        self.overflow_count = 0

    def begin(self, packet_id, send_since_start_ms):
        self.attempted = packet_id
        if packet_id > self.max_rows:
            self.overflow_count += 1
            if self.overflow_count == 1:
                print("RTT log capacity reached. This recording is incomplete.")
            return
        offset = (packet_id - 1) * 3 # Each packet uses three consecutive position. 2000x3x4=24000
        self.rows[offset] = send_since_start_ms
        self.rows[offset + 1] = 0
        self.rows[offset + 2] = self.PENDING

    def finish(self, packet_id, status, rtt_us = 0):
        if 1 <= packet_id <= min(self.attempted, self.max_rows):
            offset = (packet_id - 1) * 3
            self.rows[offset + 1] = rtt_us
            self.rows[offset + 2] = status

    def save(self):
        number = 1
        while True:
            filename = "rtt_%dms_%03d.csv" % (self.interval_ms, number)
            try:
                os.stat(filename)
            except OSError as exc:
                if exc.args[0] != errno.ENOENT:
                    raise
                break
            number += 1

        count = min(self.attempted, self.max_rows)
        with open(filename, "w") as output:
            output.write("packet_id,send_since_start_ms,rtt_ms,status,"
                         "send_interval_ms,log_overflow_count\n")
            for index in range(count):
                offset = index * 3
                status = self.rows[offset + 2]
                rtt_us = self.rows[offset + 1]
                # Missing RTT is blank, never zero. Late ACKs retain their RTT
                # but must be excluded from statistics of on-time ACKs.
                rtt_text = ("%d.%03d" % (rtt_us // 1000, rtt_us % 1000)
                            if status in (self.OK, self.LATE_ACK) else "")
                output.write("%d,%d,%s,%s,%d,%d\n" % (
                    index + 1, self.rows[offset], rtt_text,
                    self.STATUS_NAMES[status], self.interval_ms,
                    self.overflow_count))
        return filename, count


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
# UDP_PORT = "YOUR_UDP_PORT"

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

def trigger_sensor():
    global data_ready
    data_ready = False

    # Trigger distance measurement
    launch.low()
    time.sleep_us(2)
    launch.high()
    time.sleep_us(10)
    launch.low()

# 4. Packet ID
packet_id = 0

def packet_ID():
    global packet_id
    packet_id += 1
    return packet_id


# ---Implementation---

wifi_connect()
print("Sensing/send interval (ms):", SEND_INTERVAL_MS)

pending = {}
timeout_count = 0
send_error_count = 0
ok_count = 0
logger = None

try:
    logger = RttLog(SEND_INTERVAL_MS, MAX_LOG_ROWS)
    # ②.【Bound Interrupt】: Trigger on both rising and falling edges
    echo.irq(trigger=Pin.IRQ_RISING | Pin.IRQ_FALLING, handler = echo_handler)
    session_start_ms = time.ticks_ms()
    last_send_tick = session_start_ms
    last_trigger_tick = session_start_ms

    while True:
        current_tick = time.ticks_ms()

        # 1. Trigger on sensor
        if time.ticks_diff(current_tick, last_trigger_tick) >= SEND_INTERVAL_MS:
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
        if time.ticks_diff(current_tick, last_send_tick) >= SEND_INTERVAL_MS:
            last_send_tick = current_tick

            packet_id = packet_ID()

            data = {
                "packet_id": packet_id,
                #"speed": round(random.uniform(0,30), 2),
                "raw_distance": round(last_dis, 2)
            }
            message = json.dumps(data) # *Dump to String*
            bytes = message.encode()
            # Row bookkeeping happens BEFORE the RTT going.
            logger.begin(packet_id, time.ticks_diff(
                time.ticks_ms(), session_start_ms))
            # Time at sending
            pending[packet_id] = time.ticks_us() # **After Encode**
            
            try:
                sock.sendto(bytes, (UDP_IP, UDP_PORT)) # Encode(to bytes)
            except OSError as e:
                # Delete data in pending that sending fault
                pending.pop(packet_id)
                send_error_count += 1
                logger.finish(packet_id, RttLog.SEND_ERROR)
                print("Send error:", packet_id, e)
            else:
                if PRINT_EACH_PACKET:
                    print("Sent:", message)

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

                        if cost_us < CHECK_TIMEOUT_US:
                            ok_count += 1
                            logger.finish(checking_id, RttLog.OK, cost_us)
                            if PRINT_EACH_PACKET:
                                rtt_ms = cost_us / 1000
                                print("RTT: ", rtt_ms, "ms | ID: ", checking_id)
                        else:
                            timeout_count += 1
                            logger.finish(checking_id, RttLog.LATE_ACK, cost_us)
                            print("Late checking: ", checking_id)
            except ValueError:
                print("Invaild checking JSON")

        # 5. Remove records that timeout
        now_us = time.ticks_us()

        for pending_id in list(pending): #pending_id: avoid to override 'packet_id'
            if time.ticks_diff(now_us, pending[pending_id]) >= CHECK_TIMEOUT_US:
                pending.pop(pending_id)
                timeout_count += 1
                logger.finish(pending_id, RttLog.TIMEOUT)
                print("Checking timeout: ", pending_id)

except KeyboardInterrupt:
    print("Test stopped.")

finally:
    # Stopping does not turn timeout packets into measured timeouts.
        echo.irq(handler=None)
        launch.low()
        sock.close()
        print("Packets attempted:", packet_id)
        print("On-time checking:", ok_count)
        print("Total checking timeout:", timeout_count)
        print("Send errors:", send_error_count)
        print("Pending checking at stop:", len(pending))
        if logger is not None:
            try:
                filename, count = logger.save()
            except OSError as exc:
                print("RTT CSV SAVE FAILED:", exc)
                print("Keep the Pico powered; logger still contains the rows in RAM.")
            else:
                print("Saved RTT CSV on Pico:", filename)
                print("Rows saved:", count)
                print("Log overflow count:", logger.overflow_count)
                if logger.overflow_count:
                    print("INCOMPLETE LOG: repeat with a shorter recording.")