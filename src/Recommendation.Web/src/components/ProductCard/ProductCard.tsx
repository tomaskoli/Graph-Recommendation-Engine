import type { ProductDto } from '../../services/api';
import './ProductCard.css';

interface ProductCardProps {
  product: ProductDto;
  onClick?: () => void;
  score?: number;
}

export function ProductCard({ product, onClick, score }: ProductCardProps) {
  return (
    <article className="product-card" onClick={onClick}>
      <div className="product-card__header">
        <div className="product-card__ids">
          <span className="product-card__id">ID: {product.productId}</span>
          {product.categoryId && <span className="product-card__id">Cat: {product.categoryId}</span>}
          {product.brandId && <span className="product-card__id">Brand: {product.brandId}</span>}
        </div>
        {score !== undefined && (
          <span className="product-card__score">{score.toFixed(4)}</span>
        )}
      </div>
      <div className="product-card__content">
        <h3 className="product-card__title">{product.productName}</h3>
        {product.brandName && (
          <span className="product-card__brand">{product.brandName}</span>
        )}
        {product.productDescription && (
          <p className="product-card__description">{product.productDescription}</p>
        )}
      </div>
    </article>
  );
}
