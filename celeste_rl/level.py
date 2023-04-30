import json
import matplotlib.pyplot as plt
import numpy as np
import cv2
import json

class LevelRenderer:
    
    TILE_SIZE = 8
    VOID_TEXTURES = [10]
    
    
    ID_MAP = {'CrumblePlatform': 1,
             'DashBlock': 2,
             'FallingBlock': 3,
             'JumpthruPlatform': 4,
             'Player': 5,
             'Refill': 6,
             'Spikes': 7,
             'Spring': 8,
             'ZipMover': 9,
             }
        
    max_idx = max(ID_MAP.values())
    entity_values = range(max_idx+1)

    norm = plt.Normalize(vmin=0, vmax=max_idx)
    cm = plt.cm.nipy_spectral
        
    def __init__(self, img, entities, bounds, scale=1, vision_size=32):
        
        self.img = np.zeros((img.shape[0], img.shape[1], LevelRenderer.max_idx+1))
        self.img[:,:,0] = img
        self.bounds = bounds
        self.entities = entities
        self.scale = scale
        self.vision_size = vision_size
        
        for entity in self.entities:
            if entity['Name'] in LevelRenderer.ID_MAP:
                self.generic_handler(entity)
                
        
    def render_around_player(self):
        PADSIZE = (self.vision_size//2+1)*self.scale
        OFFSET = self.vision_size//2*self.scale
        
        pad0 =  np.pad(self.img[:, :, 0:1], ((PADSIZE,),(PADSIZE,), (0,)), mode="edge")
        padrest =  np.pad(self.img[:,:,1:], ((PADSIZE,),(PADSIZE,), (0,)))

        padded = np.dstack((pad0, padrest))
        ys, xs = np.where(padded[:,:,LevelRenderer.ID_MAP['Player']])

        if len(xs) > 0 and len(ys) > 0:
            y = ys[0]
            x = xs[0]
            
            
        # player is spawning, set to bottom left corner
        else:
            y = self.img.shape[0]-1 + OFFSET
            x = 0 + OFFSET
            
        return padded[y-OFFSET:y+OFFSET,
                      x-OFFSET:x+OFFSET]
    
    @classmethod
    def from_payload(cls, obs_dic, scale, vision_size):
        entities = [dict(ent, Name=ent['Name'].split('Celeste.')[-1]) for ent in obs_dic['entities']]
        bounds = json.loads(obs_dic['bounds']
                  .replace(' ', ',')
                  .replace('X', '"X"')
                  .replace('Height', '"Height"')
                  .replace('Width', '"Width"')
                  .replace('Y', '"Y"')
                 )
        width = int(np.ceil(bounds['Width']/LevelRenderer.TILE_SIZE))
        height = int(np.ceil(bounds['Height']/LevelRenderer.TILE_SIZE))

        solids = np.array([list(x + ('0'*(width - len(x)))) for x in obs_dic['solids'].split('\n')])
        solids = np.where(solids == "0", 0, 1)
        solids = cv2.resize(solids.astype(float), (solids.shape[1]*scale, solids.shape[0]*scale), interpolation = cv2.INTER_AREA)

        return cls(solids, entities, bounds, scale=scale, vision_size=vision_size)

    @staticmethod
    def create_obs(obs_dic, scale, vision_size):
        original_obs = LevelRenderer.from_payload(obs_dic, scale, vision_size)
        
        full_obs = {
            'image':original_obs.render_around_player().transpose(2,0,1),
            'climbing': np.array(int(obs_dic['climbing'])),
            'canDash': np.array(int(obs_dic['canDash'])),
            'speeds': np.array([float(x) for x in obs_dic['speed'].split(', ')])}
        return full_obs
        
    @staticmethod
    def plot_obs(obs, method='plt'):
        img = LevelRenderer.cm(LevelRenderer.norm(obs[0] + obs.argmax(0)))
        
        if method == 'plt':
            plt.imshow(img)
        else:
            cv2.imshow('observation', img)
            cv2.waitKey(1)
        
    @staticmethod
    def color_to_idx(img):
    
        color_dict = {i:tuple([int(255*x) for x in LevelRenderer.cm(LevelRenderer.norm(i))]) for i in LevelRenderer.entity_values}
        rev_dict = {b:a for a,b in color_dict.items()}

        restruc = nlr.unstructured_to_structured(img).astype('O')

        return np.vectorize(rev_dict.get)(restruc)

    def render_finish(self, dim1, dim2):
        tmp = self.img.copy()
        tmp[dim1, dim2] = LevelRenderer.ID_MAP['finish']
        return LevelRenderer.cm(LevelRenderer.norm(tmp))
    
    def generic_handler(self, entity, x_offset=0, y_offset=0, width_override=None, height_override=None):

        SCALE = self.scale / LevelRenderer.TILE_SIZE
        
        left = int(np.floor((float(entity['Left']) - self.bounds['X']) * SCALE))
        right = int(np.ceil((float(entity['Right']) - self.bounds['X']) * SCALE))
        top = int(np.floor((float(entity['Top']) - self.bounds['Y']) * SCALE))
        bottom = int(np.ceil((float(entity['Bottom']) - self.bounds['Y']) * SCALE))

        self.img[top:bottom, left:right, LevelRenderer.ID_MAP[entity['Name']]] = 1
