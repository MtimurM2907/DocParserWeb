import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { fetchPagePreviewBlob } from '../api/backend';

type FitMode = 'fit-page' | 'fit-width' | 'custom';

const ZOOM_STEP = 0.15;
const ZOOM_MIN = 0.25;
const ZOOM_MAX = 2.5;

export function DocumentOriginalViewer({
  documentId,
  authToken,
  pageCount,
}: {
  documentId: number;
  authToken: string;
  pageCount: number;
}) {
  const viewportRef = useRef<HTMLDivElement>(null);
  const [previewPage, setPreviewPage] = useState(1);
  const [pageInput, setPageInput] = useState('1');
  const [previewUrl, setPreviewUrl] = useState<string | null>(null);
  const [previewError, setPreviewError] = useState<string | null>(null);
  const [previewLoading, setPreviewLoading] = useState(false);
  const [imgNatural, setImgNatural] = useState<{ w: number; h: number } | null>(null);
  const [viewportSize, setViewportSize] = useState({ w: 0, h: 0 });
  const [fitMode, setFitMode] = useState<FitMode>('fit-page');
  const [customScale, setCustomScale] = useState(1);

  const safePage = Math.min(Math.max(1, previewPage), pageCount);

  useEffect(() => {
    setPreviewPage(1);
    setPageInput('1');
    setFitMode('fit-page');
    setCustomScale(1);
  }, [documentId, pageCount]);

  useEffect(() => {
    setPageInput(String(safePage));
  }, [safePage]);

  useEffect(() => {
    viewportRef.current?.scrollTo({ top: 0, left: 0 });
  }, [safePage, previewUrl]);

  useEffect(() => {
    const el = viewportRef.current;
    if (!el) return;
    const update = () => setViewportSize({ w: el.clientWidth, h: el.clientHeight });
    update();
    const ro = new ResizeObserver(update);
    ro.observe(el);
    return () => ro.disconnect();
  }, []);

  useEffect(() => {
    let cancelled = false;
    let objectUrl: string | null = null;
    setPreviewLoading(true);
    setPreviewError(null);
    setImgNatural(null);

    void fetchPagePreviewBlob(authToken, documentId, safePage)
      .then((blob) => {
        if (cancelled) return;
        objectUrl = URL.createObjectURL(blob);
        setPreviewUrl(objectUrl);
      })
      .catch((e) => {
        if (!cancelled) {
          setPreviewUrl(null);
          setPreviewError(e instanceof Error ? e.message : 'Не удалось загрузить страницу');
        }
      })
      .finally(() => {
        if (!cancelled) setPreviewLoading(false);
      });

    return () => {
      cancelled = true;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [authToken, documentId, safePage]);

  const displayScale = useMemo(() => {
    if (!imgNatural || viewportSize.w < 1 || viewportSize.h < 1) return 1;
    const pad = 32;
    const availW = Math.max(120, viewportSize.w - pad);
    const availH = Math.max(120, viewportSize.h - pad);
    if (fitMode === 'fit-width') {
      return Math.min(ZOOM_MAX, Math.max(ZOOM_MIN, availW / imgNatural.w));
    }
    if (fitMode === 'fit-page') {
      const s = Math.min(availW / imgNatural.w, availH / imgNatural.h);
      return Math.min(ZOOM_MAX, Math.max(ZOOM_MIN, s));
    }
    return Math.min(ZOOM_MAX, Math.max(ZOOM_MIN, customScale));
  }, [fitMode, customScale, imgNatural, viewportSize]);

  const zoomPercent = Math.round(displayScale * 100);

  const goPage = useCallback(
    (page: number) => {
      const next = Math.min(pageCount, Math.max(1, page));
      setPreviewPage(next);
      setPageInput(String(next));
      setFitMode('fit-page');
    },
    [pageCount],
  );

  const scaleRef = useRef(displayScale);
  scaleRef.current = displayScale;

  const zoomBy = useCallback((delta: number) => {
    setFitMode('custom');
    setCustomScale(Math.min(ZOOM_MAX, Math.max(ZOOM_MIN, scaleRef.current + delta)));
  }, []);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const tag = (e.target as HTMLElement | null)?.tagName;
      if (tag === 'INPUT' || tag === 'TEXTAREA') return;
      if (e.key === 'ArrowLeft') {
        e.preventDefault();
        goPage(safePage - 1);
      } else if (e.key === 'ArrowRight') {
        e.preventDefault();
        goPage(safePage + 1);
      } else if (e.key === '+' || e.key === '=') {
        e.preventDefault();
        zoomBy(ZOOM_STEP);
      } else if (e.key === '-') {
        e.preventDefault();
        zoomBy(-ZOOM_STEP);
      } else if (e.key === '0') {
        e.preventDefault();
        setFitMode('fit-page');
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [goPage, safePage, zoomBy]);

  const commitPageInput = () => {
    const parsed = parseInt(pageInput.trim(), 10);
    if (!Number.isFinite(parsed)) {
      setPageInput(String(safePage));
      return;
    }
    goPage(parsed);
  };

  const imgStyle = imgNatural
    ? {
        width: Math.round(imgNatural.w * displayScale),
        height: Math.round(imgNatural.h * displayScale),
      }
    : undefined;

  return (
    <div className="doc-text-source doc-text-source--fullscreen" aria-label="Оригинал PDF">
      <div className="doc-text-source__toolbar">
        <div className="doc-text-source__nav" aria-label="Страницы">
          <button
            type="button"
            className="btn-secondary btn-sm"
            disabled={safePage <= 1}
            onClick={() => goPage(safePage - 1)}
            title="Предыдущая страница (←)"
          >
            ←
          </button>
          <label className="doc-text-source__page-goto">
            <span className="visually-hidden">Номер страницы</span>
            <input
              type="number"
              className="doc-text-source__page-input"
              min={1}
              max={pageCount}
              value={pageInput}
              onChange={(e) => setPageInput(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter') {
                  e.preventDefault();
                  commitPageInput();
                }
              }}
              onBlur={commitPageInput}
            />
          </label>
          <span className="doc-text-source__page-total">/ {pageCount}</span>
          <button
            type="button"
            className="btn-secondary btn-sm"
            disabled={safePage >= pageCount}
            onClick={() => goPage(safePage + 1)}
            title="Следующая страница (→)"
          >
            →
          </button>
        </div>

        <div className="doc-text-source__zoom" aria-label="Масштаб">
          <button
            type="button"
            className={`btn-secondary btn-sm${fitMode === 'fit-page' ? ' active' : ''}`}
            onClick={() => setFitMode('fit-page')}
            title="Вся страница на экране (0)"
          >
            Вся страница
          </button>
          <button
            type="button"
            className={`btn-secondary btn-sm${fitMode === 'fit-width' ? ' active' : ''}`}
            onClick={() => setFitMode('fit-width')}
          >
            По ширине
          </button>
          <button type="button" className="btn-secondary btn-sm" onClick={() => zoomBy(-ZOOM_STEP)} title="Уменьшить (−)">
            −
          </button>
          <span className="doc-text-source__zoom-label">{zoomPercent}%</span>
          <button type="button" className="btn-secondary btn-sm" onClick={() => zoomBy(ZOOM_STEP)} title="Увеличить (+)">
            +
          </button>
        </div>
      </div>

      <div ref={viewportRef} className="doc-text-source__viewport">
        {previewLoading && <p className="registry-meta doc-text-source__status">Загрузка страницы…</p>}
        {previewError && <p className="doc-text-source__error">{previewError}</p>}
        {previewUrl && !previewLoading && (
          <div className="doc-text-source__page-stage">
            <img
              src={previewUrl}
              alt={`Страница ${safePage}`}
              className="doc-text-source__img"
              style={imgStyle}
              onLoad={(e) => {
                const img = e.currentTarget;
                setImgNatural({ w: img.naturalWidth, h: img.naturalHeight });
                setFitMode('fit-page');
              }}
            />
          </div>
        )}
      </div>
    </div>
  );
}

