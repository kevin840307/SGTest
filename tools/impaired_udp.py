"""Deterministic real UDP impairment; one isolated ENet route per client."""
import heapq
import random
import select
import socket
import threading
import time

class ImpairedUDP:
    def __init__(self, count, server_port=27888, delay_ms=70, jitter_ms=25, loss=0.05):
        self.rng = random.Random(8413)
        self.routes = []
        self.pending = []
        self.running = True
        self.received = self.dropped = self.forwarded = 0
        self.delay = delay_ms/1000
        self.jitter = jitter_ms/1000
        self.loss = loss
        self.ports = []
        for _ in range(count):
            front = socket.socket(socket.AF_INET,socket.SOCK_DGRAM)
            front.bind(('127.0.0.1',0))
            back = socket.socket(socket.AF_INET,socket.SOCK_DGRAM)
            back.connect(('127.0.0.1',server_port))
            self.routes.append([front,back,None])
            self.ports.append(front.getsockname()[1])
        self.thread = threading.Thread(target=self.run,daemon=True)
        self.thread.start()

    def run(self):
        sockets = [sock for route in self.routes for sock in route[:2]]
        while self.running:
            ready,_,_ = select.select(sockets,[],[],0.002)
            for sock in ready:
                try:
                    data,source = sock.recvfrom(65535)
                except ConnectionResetError:
                    continue
                for route in self.routes:
                    front,back,client = route
                    if sock is front:
                        route[2] = source
                        destination,endpoint = back,None
                        break
                    if sock is back:
                        destination,endpoint = front,client
                        break
                self.received += 1
                if endpoint is None and destination is front:
                    continue
                if self.rng.random() < self.loss:
                    self.dropped += 1
                    continue
                due = time.monotonic()+max(0,self.delay+self.rng.uniform(-self.jitter,self.jitter))
                heapq.heappush(self.pending,(due,self.received,destination,endpoint,data))
            now = time.monotonic()
            while self.pending and self.pending[0][0] <= now:
                _,_,sock,endpoint,data = heapq.heappop(self.pending)
                if endpoint is None:
                    sock.send(data)
                else:
                    sock.sendto(data,endpoint)
                self.forwarded += 1

    def close(self):
        self.running = False
        self.thread.join(timeout=2)
        for route in self.routes:
            for sock in route[:2]:
                sock.close()
        return {'one_way_delay_ms':self.delay*1000,'jitter_ms':self.jitter*1000,
                'configured_loss':self.loss,'received':self.received,'dropped':self.dropped,
                'forwarded':self.forwarded,'reordering':True}
