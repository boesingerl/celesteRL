import json
import matplotlib.pyplot as plt
import numpy as np
import cv2
import json

class LevelRenderer:
    
    TILE_SIZE = 8
    RESCALE = 4
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
    VISION_SIZE = 48
        
    max_idx = max(ID_MAP.values())
    entity_values = range(max_idx+1)

    norm = plt.Normalize(vmin=0, vmax=max_idx)
    cm = plt.cm.nipy_spectral
        
    def __init__(self, img, entities, bounds):
        
        self.img = np.zeros((img.shape[0], img.shape[1], LevelRenderer.max_idx+1))
        self.img[:,:,0] = img
        self.bounds = bounds
        self.entities = entities
        
        for entity in self.entities:
            if entity['Name'] in LevelRenderer.ID_MAP:
                self.generic_handler(entity)
                
        
    def render_around_player(self):
        PADSIZE = (LevelRenderer.VISION_SIZE//2+1)*LevelRenderer.RESCALE
        OFFSET = LevelRenderer.VISION_SIZE//2*LevelRenderer.RESCALE
        
        pad0 =  np.pad(self.img[:, :, 0:1], ((PADSIZE,),(PADSIZE,), (0,)), constant_values=1)
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
    def from_payload(cls, obs_dic):
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
        solids = cv2.resize(solids.astype(float), (solids.shape[1]*LevelRenderer.RESCALE, solids.shape[0]*LevelRenderer.RESCALE), interpolation = cv2.INTER_AREA)

        return cls(solids, entities, bounds)

    @staticmethod
    def create_obs(obs_dic):
        original_obs = LevelRenderer.from_payload(obs_dic)
        return original_obs.render_around_player().transpose(2,0,1)
        
    @staticmethod
    def plot_obs(obs):
        plt.imshow(obs[0] + obs.argmax(0), cmap=LevelRenderer.cm, norm=LevelRenderer.norm)
        
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

        SCALE = LevelRenderer.RESCALE / LevelRenderer.TILE_SIZE
        
        left = int(np.floor((float(entity['Left']) - self.bounds['X']) * SCALE))
        right = int(np.ceil((float(entity['Right']) - self.bounds['X']) * SCALE))
        top = int(np.floor((float(entity['Top']) - self.bounds['Y']) * SCALE))
        bottom = int(np.ceil((float(entity['Bottom']) - self.bounds['Y']) * SCALE))

        self.img[top:bottom, left:right, LevelRenderer.ID_MAP[entity['Name']]] = 1
