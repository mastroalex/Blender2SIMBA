import h5py
import numpy as np

NFRAMES = 60

with h5py.File("Examples/dynamic_topology_test.h5", "w") as h5:

    for f in range(NFRAMES):

        g = h5.create_group(f"Time_{f:04d}")

        t = 2*np.pi*f/(NFRAMES-1)

        nodes = np.array([
            [0.0,0.0,0.0],
            [1.0,0.0,0.0],
            [0.0,1.0,0.15*np.sin(t)],
            [1.0,1.0,0.15*np.cos(t)],
        ],dtype=np.float32)

        if f < NFRAMES//2:

            conn = np.array([
                [0,1,2],
                [1,3,2],
            ],dtype=np.int32)

        else:

            conn = np.array([
                [0,1,3],
                [0,3,2],
            ],dtype=np.int32)

        stress = np.linspace(0,1,4,dtype=np.float32)+0.2*np.sin(t)

        g.create_dataset("Nodes",data=nodes)
        g.create_dataset("Connectivity",data=conn)
        g.create_dataset("Stress",data=stress)