import zmq
import numpy as np
import matplotlib.pyplot as plt
import cv2

import PIL.Image as Image
import io
import base64
import json

# Python program to explain cv2.namedWindow() method
  
# Importing OpenCV
import cv2
  

  
# Using namedWindow()
# A window with 'Display' name is created
# with WINDOW_AUTOSIZE, window size is set automatically
cv2.namedWindow("Display", cv2.WINDOW_NORMAL)
  


context = zmq.Context()
socket = context.socket(zmq.REQ)
socket.connect("tcp://localhost:7777")

while True:
    
    socket.send_string(json.dumps([1]))
    pack = socket.recv()

    obs = json.loads(pack)[0]
    img = np.array(Image.open(io.BytesIO(base64.b64decode(obs))))[..., :3]
    
    # using cv2.imshow() to display the image
    cv2.imshow('Display', img[...,::-1])

    # Waiting 0ms for user to press any key
    cv2.waitKey(1)
    
    