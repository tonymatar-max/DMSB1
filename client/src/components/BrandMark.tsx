interface BrandMarkProps {
  size?: 'sm' | 'lg';
}

export default function BrandMark({ size = 'sm' }: BrandMarkProps) {
  return (
    <div className={`brand-mark${size === 'lg' ? ' large' : ''}`}>
      <span className="brand-node" />
    </div>
  );
}
