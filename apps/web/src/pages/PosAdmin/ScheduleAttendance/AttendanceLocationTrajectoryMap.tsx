import { Alert, Spin } from 'antd'
import { useEffect, useRef, useState } from 'react'
import type { Map as LeafletMap } from 'leaflet'
import 'leaflet/dist/leaflet.css'

export type AttendanceTrajectoryMapPointKind = 'ClockIn' | 'Sample' | 'ClockOut'

export interface AttendanceTrajectoryMapPoint {
  key: string
  kind: AttendanceTrajectoryMapPointKind
  latitude: number
  longitude: number
  label: string
  accuracy?: number
}

interface AttendanceLocationTrajectoryMapProps {
  points: AttendanceTrajectoryMapPoint[]
  ariaLabel: string
  loadFailedText: string
  tileLoadFailedText: string
}

const pointColors: Record<AttendanceTrajectoryMapPointKind, string> = {
  ClockIn: '#52c41a',
  Sample: '#1677ff',
  ClockOut: '#ff4d4f',
}

export default function AttendanceLocationTrajectoryMap({
  points,
  ariaLabel,
  loadFailedText,
  tileLoadFailedText,
}: AttendanceLocationTrajectoryMapProps) {
  const containerRef = useRef<HTMLDivElement | null>(null)
  const [loading, setLoading] = useState(true)
  const [loadFailed, setLoadFailed] = useState(false)
  const [tileLoadFailed, setTileLoadFailed] = useState(false)

  useEffect(() => {
    let disposed = false
    let map: LeafletMap | undefined

    setLoading(true)
    setLoadFailed(false)
    setTileLoadFailed(false)

    const initializeMap = async () => {
      try {
        const leafletModule = await import('leaflet')
        if (disposed || !containerRef.current) return

        const reducedMotion = window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false
        map = leafletModule.map(containerRef.current, {
          zoomAnimation: !reducedMotion,
          fadeAnimation: !reducedMotion,
          markerZoomAnimation: !reducedMotion,
        })

        const tileLayer = leafletModule.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
          maxZoom: 19,
          attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
        })
        tileLayer.on('tileerror', () => {
          if (!disposed) setTileLoadFailed(true)
        })
        tileLayer.addTo(map)

        const latLngs = points.map((point) => leafletModule.latLng(point.latitude, point.longitude))
        points.forEach((point, index) => {
          leafletModule.circleMarker(latLngs[index], {
            radius: point.kind === 'Sample' ? 5 : 7,
            color: pointColors[point.kind],
            fillColor: pointColors[point.kind],
            fillOpacity: 0.9,
            weight: 2,
          })
            .bindTooltip(point.label, { direction: 'top' })
            .addTo(map!)

          if (typeof point.accuracy === 'number' && point.accuracy > 0) {
            leafletModule.circle(latLngs[index], {
              radius: point.accuracy,
              color: pointColors[point.kind],
              fillOpacity: 0.04,
              opacity: 0.25,
              weight: 1,
              interactive: false,
            }).addTo(map!)
          }
        })

        if (latLngs.length >= 2) {
          leafletModule.polyline(latLngs, {
            color: '#1677ff',
            opacity: 0.75,
            weight: 4,
          }).addTo(map)
          map.fitBounds(leafletModule.latLngBounds(latLngs), {
            animate: !reducedMotion,
            maxZoom: 18,
            padding: [28, 28],
          })
        } else if (latLngs[0]) {
          map.setView(latLngs[0], 17, { animate: !reducedMotion })
        }

        if (!disposed) setLoading(false)
      } catch (error) {
        console.error('Attendance trajectory map initialization failed', error)
        if (!disposed) {
          setLoading(false)
          setLoadFailed(true)
        }
      }
    }

    void initializeMap()
    return () => {
      disposed = true
      map?.remove()
    }
  }, [points])

  if (loadFailed) {
    return <Alert type="warning" showIcon message={loadFailedText} />
  }

  return (
    <div style={{ position: 'relative' }}>
      {tileLoadFailed ? (
        <Alert
          type="warning"
          showIcon
          message={tileLoadFailedText}
          style={{ marginBottom: 8 }}
        />
      ) : null}
      <div
        ref={containerRef}
        aria-label={ariaLabel}
        style={{
          width: '100%',
          height: 360,
          border: '1px solid #f0f0f0',
          borderRadius: 8,
          overflow: 'hidden',
        }}
      />
      {loading ? (
        <div
          style={{
            position: 'absolute',
            inset: tileLoadFailed ? 48 : 0,
            display: 'grid',
            placeItems: 'center',
            background: 'rgba(255, 255, 255, 0.7)',
            pointerEvents: 'none',
          }}
        >
          <Spin />
        </div>
      ) : null}
    </div>
  )
}
