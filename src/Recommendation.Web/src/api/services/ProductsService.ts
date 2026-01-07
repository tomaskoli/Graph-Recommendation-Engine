/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { PaginatedResultOfProductDto } from '../models/PaginatedResultOfProductDto';
import type { ProductDetailDto } from '../models/ProductDetailDto';
import type { CancelablePromise } from '../core/CancelablePromise';
import { OpenAPI } from '../core/OpenAPI';
import { request as __request } from '../core/request';
export class ProductsService {
    /**
     * @returns ProductDetailDto OK
     * @throws ApiError
     */
    public static getProductById({
        id,
    }: {
        id: number,
    }): CancelablePromise<ProductDetailDto> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/products/{id}',
            path: {
                'id': id,
            },
            errors: {
                404: `Not Found`,
            },
        });
    }
    /**
     * @returns PaginatedResultOfProductDto OK
     * @throws ApiError
     */
    public static getRelatedProducts({
        id,
        page,
        pageSize,
    }: {
        id: number,
        page: number | string,
        pageSize: number | string,
    }): CancelablePromise<PaginatedResultOfProductDto> {
        return __request(OpenAPI, {
            method: 'GET',
            url: '/api/products/{id}/related',
            path: {
                'id': id,
            },
            query: {
                'page': page,
                'pageSize': pageSize,
            },
        });
    }
}
