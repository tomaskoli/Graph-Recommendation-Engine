import type { ProductDto, ScoredProductDto } from '../../services/api';
import { ProductCard } from '../ProductCard';
import './RecommendationList.css';

interface RecommendationListProps {
  title: string;
  products: ScoredProductDto[] | ProductDto[];
  onProductClick?: (product: ProductDto) => void;
  showScore?: boolean;
}

export function RecommendationList({ title, products, onProductClick, showScore }: RecommendationListProps) {
  if (products.length === 0) {
    return null;
  }

  return (
    <section className="recommendation-list">
      <h3 className="recommendation-list__title">{title}</h3>
      <div className="recommendation-list__scroll">
        <div className="recommendation-list__grid">
          {products.map(product => {
            const score = showScore && 'similarityScore' in product 
              ? Number(product.similarityScore) 
              : undefined;
            
            return (
              <ProductCard
                key={product.productId}
                product={product as ProductDto}
                onClick={() => onProductClick?.(product as ProductDto)}
                score={score}
              />
            );
          })}
        </div>
      </div>
    </section>
  );
}
