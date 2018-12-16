#pragma once

#ifndef _H_AMEISENNAVIGATION
#define _H_AMEISENNAVIGATION

#include <iostream>
#include <fstream>
#include <sstream>
#include <iomanip>
#include <map>
#include <vector>
#include <chrono>

#include <windows.h>

#include "../recastnavigation/Detour/Include/DetourNavMesh.h"
#include "../recastnavigation/Detour/Include/DetourNavMeshQuery.h"

constexpr int MMAP_MAGIC = 0x4d4d4150;
constexpr int MMAP_VERSION = 6;

enum NavTerrain
{
	NAV_EMPTY = 0x00,
	NAV_GROUND = 0x01,
	NAV_MAGMA = 0x02,
	NAV_SLIME = 0x04,
	NAV_WATER = 0x08,
	NAV_UNUSED1 = 0x10,
	NAV_UNUSED2 = 0x20,
	NAV_UNUSED3 = 0x40,
	NAV_UNUSED4 = 0x80
};

struct MmapTileHeader {
	unsigned int mmapMagic;
	unsigned int dtVersion;
	unsigned int mmapVersion;
	unsigned int size;
	char usesLiquids;
	char padding[3];
};

class AmeisenNavigation {
private:
	std::string _mmap_dir;
	std::map<int, dtNavMesh*> _meshmap;
	std::map<int, dtNavMeshQuery*> _querymap;
	dtQueryFilter _filter;

	std::string format_trailing_zeros(int number, int total_count);

	void RDToWoWCoords(float pos[]);
	void WoWToRDCoords(float pos[]);

public:
	AmeisenNavigation(std::string mmap_dir);

	void GetPath(int map_id, float* start, float* end, float** path, int* path_size);
	dtPolyRef GetNearestPoly(int map_id, float* pos, float* closest_point);

	bool LoadMmapsForContinent(int map_id);
};
#endif
