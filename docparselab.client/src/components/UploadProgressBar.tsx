type Props = {
  percent: number;
  label: string;
};

export function UploadProgressBar({ percent, label }: Props) {
  const safe = Math.min(100, Math.max(0, Math.round(percent)));

  return (
    <div
      className="upload-progress"
      role="progressbar"
      aria-valuenow={safe}
      aria-valuemin={0}
      aria-valuemax={100}
      aria-label={label}
    >
      <div className="upload-progress__head">
        <span className="upload-progress__label">{label}</span>
        <span className="upload-progress__pct">{safe}%</span>
      </div>
      <div className="upload-progress__track">
        <div className="upload-progress__bar" style={{ width: `${safe}%` }} />
      </div>
    </div>
  );
}
